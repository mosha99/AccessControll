// ═══════════════════════════════════════════════════
//  ESP8266 + SignalR + Keypad PCF8574 + SSD1306 OLED
//  لاگ: http://[IP_ESP]
//
//  کتابخانه‌ها:
//    1. WebSockets       by Markus Sattler
//    2. ArduinoJson      by Benoit Blanchon
//    3. Adafruit SSD1306
//    4. Adafruit GFX
//    5. micro-ecc        by Kenneth MacKay  (P-256 ECDSA verify)
//  (I2CKeyPad دیگه لازم نیست)
//
//  امنیت:
//    سرور پس از اتصال، یک nonce تصادفی را با کلید خصوصی P-256 خود امضا کرده
//    و به ESP ارسال می‌کند (AuthChallenge).
//    ESP امضا را با کلید عمومی ذخیره‌شده در EEPROM تأیید می‌کند؛
//    تنها در صورت موفقیت، RegisterStation فراخوانی می‌شود.
// ═══════════════════════════════════════════════════

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <WebSocketsClient.h>
#include <ArduinoJson.h>
#include <Wire.h>
#if CFG_STATION_TYPE != 2          // RemoteControl has no display
  #include <Adafruit_GFX.h>
  #include <Adafruit_SSD1306.h>
#endif
#include <EEPROM.h>
#include <uECC.h>                         // micro-ecc: P-256 verify
#include <bearssl/bearssl_hash.h>         // BearSSL SHA-256 (bundled with ESP8266 core)
#include "config.h"  // Build-time provisioning: CFG_WIFI_SSID, CFG_SERVER_HOST, CFG_SERVER_PORT

// ── Forward declarations ───────────────────────────
#if CFG_STATION_TYPE != 2
  void oledShowStatus(String line1, String line2 = "", String line3 = "");
#else
  inline void oledShowStatus(String, String = "", String = "") {}  // no-op for RemoteControl
#endif

// ── EEPROM Provisioning ───────────────────────────
// PROV_MAGIC 0xAE: struct with 64-byte P-256 public key (X‖Y).
// Old devices (0xAC/0xAD) will re-provision automatically on next boot.
#define PROV_MAGIC   0xAE
#define EEPROM_SIZE  256

struct __attribute__((packed)) ProvData {
  uint8_t  magic;        //   1 byte  @ offset   0
  char     ssid[33];     //  33 bytes @ offset   1
  char     password[65]; //  65 bytes @ offset  34
  char     server[65];   //  65 bytes @ offset  99
  uint16_t port;         //   2 bytes @ offset 164
  uint8_t  pubkey[64];   //  64 bytes @ offset 166  ← P-256 public key (X‖Y, 32+32)
  uint8_t  hasPubkey;    //   1 byte  @ offset 230
};                       // Total: 231 bytes (fits in EEPROM_SIZE=256)

// ── WiFi — hardcoded fallback if not provisioned ──
const char* WIFI_SSID = "$(M_2.4_M)$";
const char* WIFI_PASS = "M@ein#1383";

// ── SignalR Server (runtime — set via /connect) ────
char   g_wsHost[64] = "";
int    g_wsPort     = 5000;
const char* WS_PATH = "/hardwareHub";
bool   g_wsStarted  = false;

// ── I2C Pins (ESP-01) ─────────────────────────────
#define SDA_PIN 0
#define SCL_PIN 2

// ── OLED + Keypad (General / Door stations only) ──
#if CFG_STATION_TYPE != 2

#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
#define OLED_ADDR 0x3C
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);

// ══════════════════════════════════════════════════
//  Custom PCF8574 Keypad
// ══════════════════════════════════════════════════
#define KEYPAD_ADDR 0x20

const char KEYMAP[4][4] = {
  {'1','2','3','A'},
  {'4','5','6','B'},
  {'7','8','9','C'},
  {'*','0','#','D'}
};

char getKeyFromPCF8574() {
  static char lastKey = 0;
  static unsigned long releaseAt = 0;

  for (int row = 0; row < 4; row++) {
    uint8_t writeByte = (~(1 << row) & 0x0F) | 0xF0;
    Wire.beginTransmission(KEYPAD_ADDR);
    Wire.write(writeByte);
    Wire.endTransmission();
    delayMicroseconds(200);

    Wire.requestFrom((int)KEYPAD_ADDR, (int)1);
    if (!Wire.available()) continue;
    uint8_t cols = (Wire.read() >> 4) & 0x0F;

    for (int col = 0; col < 4; col++) {
      if (!(cols & (1 << col))) {
        delay(20);
        Wire.beginTransmission(KEYPAD_ADDR);
        Wire.write(0xFF);
        Wire.endTransmission();
        char key = KEYMAP[row][col];
        releaseAt = 0;
        if (key != lastKey) { lastKey = key; return key; }
        return 0;
      }
    }
  }

  Wire.beginTransmission(KEYPAD_ADDR);
  Wire.write(0xFF);
  Wire.endTransmission();
  if (lastKey != 0) {
    if (releaseAt == 0) releaseAt = millis();
    if (millis() - releaseAt >= 150) { lastKey = 0; releaseAt = 0; }
  }
  return 0;
}

bool initKeypad() {
  Wire.beginTransmission(KEYPAD_ADDR);
  Wire.write(0xFF);
  return Wire.endTransmission() == 0;
}

#endif  // CFG_STATION_TYPE != 2

// ── Relay (PCF8574T, non-blocking) ────────────────
uint8_t  g_relayAddr      = 0;
uint8_t  g_relayPin       = 0;
bool     g_relayActiveLow = true;   // tracks polarity for the auto-off timer
uint32_t g_relayOffAt     = 0;      // 0 = no active relay timer

// ── P-256 auth state ──────────────────────────────
// Pubkey is loaded from EEPROM in setup() and cached here for event-handler use.
static uint8_t  g_serverPubKey[64] = {0};
static bool     g_hasPubKey        = false;

// Per-session monotonic counter — resets on every disconnect.
// Each signed server message must carry a seqno strictly greater than the last accepted one.
static uint32_t g_seqnoLast = 0;
static bool     g_seqnoInit = false;   // false = first message of session, skip >-check

void relayPinOn(uint8_t addr, uint8_t pin, bool activeLow) {
  Wire.beginTransmission(addr);
  Wire.write(activeLow ? (uint8_t)(~(1 << pin)) : (uint8_t)(1 << pin));
  Wire.endTransmission();
}

void relayAllOff(uint8_t addr, bool activeLow) {
  Wire.beginTransmission(addr);
  Wire.write(activeLow ? (uint8_t)0xFF : (uint8_t)0x00);  // active-low: 0xFF=all off; active-high: 0x00=all off
  Wire.endTransmission();
}

// ── WebSocket ─────────────────────────────────────
WebSocketsClient ws;
bool wsConnected = false;

// ── Web Logger ────────────────────────────────────
ESP8266WebServer logServer(80);
String logs = "";

void logMessage(String msg) {
  Serial.println(msg);
  logs += msg + "<br>\n";
  if (logs.length() > 3000) logs = logs.substring(1500);
}

void handleRoot() {
  String html = "<!DOCTYPE html><html><head>"
    "<meta charset='utf-8'><meta http-equiv='refresh' content='2'>"
    "<style>body{background:#111;color:#0f0;font-family:monospace;padding:15px;}"
    "h2{color:#ff0;} a{color:#f80;}"
    ".box{background:#1a1a1a;padding:10px;border:1px solid #333;border-radius:5px;}"
    "</style></head><body><h2>ESP8266 Log</h2>"
    "<a href='/clear'>Clear</a> | <a href='/'>Refresh</a>"
    "<hr><div class='box'>" + logs + "</div></body></html>";
  logServer.send(200, "text/html", html);
}

void handleClear() {
  logs = "";
  logServer.sendHeader("Location", "/");
  logServer.send(302);
}

// ── Hex helpers ───────────────────────────────────

static uint8_t hexNibble(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return 0;
}

static int hexDecode(const char* hex, uint8_t* out, int maxLen) {
  int len = strlen(hex) / 2;
  if (len > maxLen) len = maxLen;
  for (int i = 0; i < len; i++)
    out[i] = (hexNibble(hex[2*i]) << 4) | hexNibble(hex[2*i+1]);
  return len;
}

// ── Crypto Helpers ────────────────────────────────

/// SHA-256 via BearSSL (bundled with ESP8266 Arduino core).
static void sha256(const uint8_t* data, size_t len, uint8_t* out32) {
  br_sha256_context ctx;
  br_sha256_init(&ctx);
  br_sha256_update(&ctx, data, len);
  br_sha256_out(&ctx, out32);
}

/// Verify a P-256 / SHA-256 signature (IEEE P1363 format: r‖s, 64 bytes).
/// pubkey64: 64-byte raw X‖Y; sig64: 64-byte r‖s; data/dataLen: signed payload.
static bool verifyP256(const uint8_t* pubkey64, const uint8_t* data, size_t dataLen, const uint8_t* sig64) {
  uint8_t hash[32];
  sha256(data, dataLen, hash);
  return uECC_verify(pubkey64, hash, 32, sig64, uECC_secp256r1()) == 1;
}

/// Verify a signed server→ESP message and enforce monotonic seqno (anti-replay).
/// sigData format signed by server: "target|arg1|arg2|...|seqno"
/// Returns true if signature is valid and seqno is acceptable; updates g_seqnoLast.
static bool verifyMsg(const char* sigData, uint32_t seqno, const char* sigHex) {
  if (!g_hasPubKey) {
    logMessage("ERR: No public key — cannot verify message");
    return false;
  }
  if (g_seqnoInit && seqno <= g_seqnoLast) {
    logMessage("ERR: Replay/reorder seq=" + String(seqno) + " last=" + String(g_seqnoLast));
    return false;
  }
  uint8_t sig[64];
  if (hexDecode(sigHex, sig, 64) != 64) { logMessage("ERR: Bad sig hex"); return false; }
  if (!verifyP256(g_serverPubKey, (const uint8_t*)sigData, strlen(sigData), sig)) {
    logMessage("ERR: Sig INVALID for: " + String(sigData));
    return false;
  }
  g_seqnoLast = seqno;
  g_seqnoInit = true;
  return true;
}

// ── OLED Helpers + Base64 (General / Door only) ───
#if CFG_STATION_TYPE != 2

void oledShowStatus(String line1, String line2, String line3) {
  display.clearDisplay();
  display.setTextColor(WHITE);
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.println(line1);
  if (line2 != "") display.println(line2);
  if (line3 != "") display.println(line3);
  display.display();
}

void oledRenderBuffer(uint8_t* buf) {
  memcpy(display.getBuffer(), buf, 1024);
  display.display();
}

static const char B64T[] =
  "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

int b64Decode(const char* in, uint8_t* out, int maxOut) {
  int n = 0, len = strlen(in);
  for (int i = 0; i + 3 < len; i += 4) {
    uint8_t a = strchr(B64T, in[i])   - B64T;
    uint8_t b = strchr(B64T, in[i+1]) - B64T;
    uint8_t c = (in[i+2]!='=') ? (uint8_t)(strchr(B64T,in[i+2])-B64T) : 0;
    uint8_t d = (in[i+3]!='=') ? (uint8_t)(strchr(B64T,in[i+3])-B64T) : 0;
    if (n < maxOut) out[n++] = (a << 2) | (b >> 4);
    if (n < maxOut && in[i+2] != '=') out[n++] = (b << 4) | (c >> 2);
    if (n < maxOut && in[i+3] != '=') out[n++] = (c << 6) | d;
  }
  return n;
}

#endif  // CFG_STATION_TYPE != 2

// ── UART Provisioning ─────────────────────────────
// Expects: PROVISION:{"ssid":"...","password":"...","server":"...","port":N,"pubkey":"128hexchars"}
void handleProvision(String json) {
  StaticJsonDocument<512> doc;
  if (deserializeJson(doc, json) != DeserializationError::Ok) {
    Serial.println("ERR:invalid JSON");
    return;
  }

  const char* ssid      = doc["ssid"]     | "";
  const char* password  = doc["password"] | "";
  const char* server    = doc["server"]   | "";
  int         port      = doc["port"]     | 5000;
  const char* pubkeyHex = doc["pubkey"]   | "";

  if (strlen(ssid) == 0) { Serial.println("ERR:missing ssid"); return; }

  ProvData pdata;
  pdata.magic = PROV_MAGIC;
  strncpy(pdata.ssid,     ssid,     sizeof(pdata.ssid)     - 1); pdata.ssid[sizeof(pdata.ssid)-1]         = '\0';
  strncpy(pdata.password, password, sizeof(pdata.password) - 1); pdata.password[sizeof(pdata.password)-1] = '\0';
  strncpy(pdata.server,   server,   sizeof(pdata.server)   - 1); pdata.server[sizeof(pdata.server)-1]     = '\0';
  pdata.port = (uint16_t)port;

  // Store server P-256 public key if provided (128 hex chars = 64 bytes X‖Y)
  if (strlen(pubkeyHex) == 128) {
    hexDecode(pubkeyHex, pdata.pubkey, 64);
    pdata.hasPubkey = 1;
    logMessage("OK: Server P-256 public key stored (" + String(pubkeyHex).substring(0, 8) + "...)");
  } else {
    memset(pdata.pubkey, 0, 64);
    pdata.hasPubkey = 0;
    logMessage("WARN: No public key in PROVISION payload — /connect will not be verified");
  }

  EEPROM.put(0, pdata);
  EEPROM.commit();

  String mac = WiFi.macAddress();
  Serial.println("OK:{\"mac\":\"" + mac + "\"}");

  logMessage("Provisioned — rebooting in 3s...");
  oledShowStatus("Provisioned!", "Rebooting...");
  delay(3000);
  ESP.restart();
}

void checkProvisionSerial() {
  if (!Serial.available()) return;
  String line = Serial.readStringUntil('\n');
  line.trim();
  if (line.startsWith("PROVISION:")) handleProvision(line.substring(10));
}

// ── SignalR ───────────────────────────────────────
void signalRHandshake() {
  String msg = "{\"protocol\":\"json\",\"version\":1}";
  msg += (char)0x1E;
  ws.sendTXT(msg);
  logMessage("SignalR: Handshake sent");
}

void registerStation() {
  String mac = WiFi.macAddress();
  StaticJsonDocument<200> doc;
  doc["type"]   = 1;
  doc["target"] = "RegisterStation";
  JsonArray args = doc.createNestedArray("arguments");
  args.add(mac);
  args.add((int)CFG_STATION_TYPE);   // firmware-baked type: 0=General, 1=Door, 2=RemoteControl
  String msg; serializeJson(doc, msg); msg += (char)0x1E;
  ws.sendTXT(msg);
  logMessage(">> RegisterStation: " + mac + " type=" + String(CFG_STATION_TYPE));
}

#if CFG_STATION_TYPE != 2
void sendKey(char key) {
  StaticJsonDocument<200> doc;
  doc["type"]   = 1;
  doc["target"] = "SendKey";
  doc.createNestedArray("arguments").add(String(key));
  String msg; serializeJson(doc, msg); msg += (char)0x1E;
  ws.sendTXT(msg);
  logMessage(">> Key sent: " + String(key));
}
#endif  // CFG_STATION_TYPE != 2

void handleServerMessage(uint8_t* payload, size_t length) {
  String raw = String((char*)payload);
  int start = 0;
  while (start < (int)raw.length()) {
    int end = raw.indexOf((char)0x1E, start);
    if (end == -1) end = raw.length();
    String chunk = raw.substring(start, end);
    start = end + 1;
    if (chunk.length() < 2) continue;

#if CFG_STATION_TYPE != 2
    if (chunk.indexOf("\"RenderDisplay\"") > 0) {
      int b64Start = chunk.indexOf("[\"") + 2;
      int b64End   = (b64Start > 1) ? chunk.indexOf('"', b64Start) : -1;
      if (b64Start < 2 || b64End <= b64Start) { logMessage("ERR: RenderDisplay fmt"); continue; }
      uint8_t* buf = (uint8_t*)malloc(1024);
      if (!buf) { logMessage("ERR: malloc"); continue; }
      int n = b64Decode(chunk.substring(b64Start, b64End).c_str(), buf, 1024);
      if (n == 1024) { oledRenderBuffer(buf); logMessage(">> Display updated"); }
      else logMessage("ERR: decoded=" + String(n));
      free(buf);
      continue;
    }
#endif  // CFG_STATION_TYPE != 2

    // ── JSON parsing (all non-RenderDisplay messages) ──
    // StaticJsonDocument<768>: must hold AuthChallenge (128-char sigHex) and
    // signed OpenDoor/CloseDoor (128-char sigHex + seqno).
    StaticJsonDocument<768> doc;
    if (deserializeJson(doc, chunk) != DeserializationError::Ok) continue;
    int type = doc["type"] | 0;

    // type 0 = SignalR handshake ACK: wait for AuthChallenge from server (do NOT register yet)
    if (type == 0) { logMessage("SignalR: Handshake ACK — awaiting AuthChallenge"); continue; }
    if (type == 6) { ws.sendTXT("{\"type\":6}\x1E"); continue; }  // SignalR ping → pong
    if (type == 1) {
      const char* target = doc["target"] | "";

      // ── AuthChallenge: verify server identity before registering ───────────
      if (strcmp(target, "AuthChallenge") == 0) {
        const char* nonceHex = doc["arguments"][0] | "";
        const char* sigHex   = doc["arguments"][1] | "";

        if (!g_hasPubKey) {
          logMessage("ERR: No public key in EEPROM — cannot verify server — disconnecting");
          ws.disconnect();
          continue;
        }
        uint8_t nonce[16], sig[64];
        int nLen = hexDecode(nonceHex, nonce, 16);
        int sLen = hexDecode(sigHex,   sig,   64);
        if (nLen != 16 || sLen != 64) {
          logMessage("ERR: AuthChallenge bad lengths nLen=" + String(nLen) + " sLen=" + String(sLen));
          ws.disconnect();
          continue;
        }
        if (!verifyP256(g_serverPubKey, nonce, 16, sig)) {
          logMessage("ERR: AuthChallenge signature INVALID — server key mismatch — disconnecting");
          ws.disconnect();
          continue;
        }
        logMessage("OK: Server identity verified (P-256) — registering");
        registerStation();

#if CFG_STATION_TYPE != 2
      } else if (strcmp(target, "PowerState") == 0) {
        bool on = doc["arguments"][0];
        Wire.beginTransmission(OLED_ADDR); Wire.write(0x00); Wire.write(on ? 0xAF : 0xAE); Wire.endTransmission();
        logMessage(">> Power: " + String(on ? "ON" : "OFF"));
#endif  // CFG_STATION_TYPE != 2

      } else if (strcmp(target, "OpenDoor") == 0) {
        uint8_t  addr    = (uint8_t)(int)(doc["arguments"][0] | 0x20);
        uint8_t  pin     = (uint8_t)(int)(doc["arguments"][1] | 0);
        int      dur     = doc["arguments"][2] | 5000;
        bool     actLow  = doc["arguments"][3] | true;
        uint32_t seq     = (uint32_t)(long)doc["arguments"][4];
        const char* sh   = doc["arguments"][5] | "";

        // Build sigData exactly as the server did: "OpenDoor|addr|pin|dur|alBit|seqno"
        char sigData[96];
        snprintf(sigData, sizeof(sigData), "OpenDoor|%d|%d|%d|%d|%lu",
                 (int)addr, (int)pin, dur, actLow ? 1 : 0, (unsigned long)seq);
        if (!verifyMsg(sigData, seq, sh)) {
          logMessage("ERR: OpenDoor rejected (bad sig/replay)");
          continue;
        }
        g_relayAddr      = addr;
        g_relayPin       = pin;
        g_relayActiveLow = actLow;
        relayPinOn(addr, pin, actLow);
        if (dur > 0) {
          g_relayOffAt = millis() + (uint32_t)dur;
          logMessage(">> Relay ON addr=0x" + String(addr, HEX) + " pin=" + String(pin)
                     + " dur=" + String(dur) + "ms al=" + String(actLow));
        } else {
          g_relayOffAt = 0;
          logMessage(">> Relay ON (toggle) addr=0x" + String(addr, HEX) + " pin=" + String(pin)
                     + " al=" + String(actLow));
        }

      } else if (strcmp(target, "CloseDoor") == 0) {
        uint8_t  addr    = (uint8_t)(int)(doc["arguments"][0] | 0x20);
        uint8_t  pin     = (uint8_t)(int)(doc["arguments"][1] | 0);
        bool     actLow  = doc["arguments"][2] | true;
        uint32_t seq     = (uint32_t)(long)doc["arguments"][3];
        const char* sh   = doc["arguments"][4] | "";

        // Build sigData exactly as the server did: "CloseDoor|addr|pin|alBit|seqno"
        char sigData[80];
        snprintf(sigData, sizeof(sigData), "CloseDoor|%d|%d|%d|%lu",
                 (int)addr, (int)pin, actLow ? 1 : 0, (unsigned long)seq);
        if (!verifyMsg(sigData, seq, sh)) {
          logMessage("ERR: CloseDoor rejected (bad sig/replay)");
          continue;
        }
        g_relayAddr      = addr;
        g_relayActiveLow = actLow;
        g_relayOffAt     = 0;
        relayAllOff(addr, actLow);
        logMessage(">> Relay OFF (toggle close) addr=0x" + String(addr, HEX) + " al=" + String(actLow));
      }
    }
  }
}

static String _fragBuf = "";

void onWsEvent(WStype_t type, uint8_t* payload, size_t length) {
  switch (type) {
    case WStype_CONNECTED:
      wsConnected = true; _fragBuf = "";
      logMessage("WS: Connected"); oledShowStatus("Connected!", g_wsHost); signalRHandshake(); break;
    case WStype_DISCONNECTED:
      wsConnected = false; _fragBuf = "";
      g_seqnoInit = false; g_seqnoLast = 0;   // reset anti-replay counter for next session
      logMessage("WS: Disconnected"); oledShowStatus("Disconnected", "Waiting server..."); break;
    case WStype_TEXT:
      handleServerMessage(payload, length); break;
    case WStype_FRAGMENT_TEXT_START:
      _fragBuf = String((char*)payload); break;
    case WStype_FRAGMENT:
      _fragBuf += String((char*)payload); break;
    case WStype_FRAGMENT_FIN:
      _fragBuf += String((char*)payload);
#if CFG_STATION_TYPE != 2
      if (_fragBuf.indexOf("\"RenderDisplay\"") > 0) {
        int b64S = _fragBuf.indexOf("[\"") + 2;
        int b64E = (b64S > 1) ? _fragBuf.indexOf('"', b64S) : -1;
        if (b64S >= 2 && b64E > b64S) {
          uint8_t* buf = (uint8_t*)malloc(1024);
          if (buf) { _fragBuf[b64E] = '\0'; int n = b64Decode(_fragBuf.c_str() + b64S, buf, 1024); if (n == 1024) oledRenderBuffer(buf); free(buf); }
        }
      } else
#endif
      { handleServerMessage((uint8_t*)_fragBuf.c_str(), _fragBuf.length()); }
      _fragBuf = ""; break;
    case WStype_ERROR: logMessage("WS: Error"); break;
    default: logMessage("WS: evt=" + String((int)type)); break;
  }
}

// ══════════════════════════════════════════════════
//  Setup
// ══════════════════════════════════════════════════
void setup() {
  Serial.setRxBufferSize(512); // PROVISION command with pubkey is ~240 chars; default 64-byte buffer overflows
  Serial.begin(115200);
  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(400000);

#if CFG_STATION_TYPE != 2
  if (!display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR)) { Serial.println("ERR: OLED"); for(;;); }
  oledShowStatus("Booting...");
#endif
  logMessage("--- Boot ---");

#if CFG_STATION_TYPE != 2
  if (initKeypad()) logMessage("OK: Keypad at 0x" + String(KEYPAD_ADDR, HEX));
  else              logMessage("ERR: Keypad not found at 0x" + String(KEYPAD_ADDR, HEX));
#endif

  EEPROM.begin(EEPROM_SIZE);
  ProvData pdata;
  EEPROM.get(0, pdata);

  // ── Auto-provision from build-time config.h (first boot only) ────────────
  // If EEPROM is blank/stale AND config.h has credentials baked in at compile
  // time, write them to EEPROM now.  Subsequent boots use EEPROM directly.
  if (pdata.magic != PROV_MAGIC && strlen(CFG_WIFI_SSID) > 0) {
    logMessage("Auto-provisioning from build-time config.h...");
    memset(&pdata, 0, sizeof(pdata));
    pdata.magic = PROV_MAGIC;
    strncpy(pdata.ssid,     CFG_WIFI_SSID,     sizeof(pdata.ssid)     - 1);
    strncpy(pdata.password, CFG_WIFI_PASSWORD,  sizeof(pdata.password) - 1);
    strncpy(pdata.server,   CFG_SERVER_HOST,    sizeof(pdata.server)   - 1);
    pdata.port = (uint16_t)CFG_SERVER_PORT;
    if (strlen(CFG_SERVER_PUBKEY) == 128) {
      hexDecode(CFG_SERVER_PUBKEY, pdata.pubkey, 64);
      pdata.hasPubkey = 1;
      logMessage("OK: P-256 pubkey loaded from build config");
    }
    EEPROM.put(0, pdata);
    EEPROM.commit();
    logMessage("EEPROM provisioned from build config");
  }
  // ─────────────────────────────────────────────────────────────────────────

  // ── Key rotation: update EEPROM pubkey if config.h has a different key ────
  // Runs even when EEPROM is already provisioned — allows key rotation after a
  // server key change without losing WiFi credentials or bumping PROV_MAGIC.
  // Just re-flash the firmware (WriteConfigHeader bakes the current server key).
  if (pdata.magic == PROV_MAGIC && strlen(CFG_SERVER_PUBKEY) == 128) {
    uint8_t cfgKey[64];
    hexDecode(CFG_SERVER_PUBKEY, cfgKey, 64);
    if (!pdata.hasPubkey || memcmp(pdata.pubkey, cfgKey, 64) != 0) {
      memcpy(pdata.pubkey, cfgKey, 64);
      pdata.hasPubkey = 1;
      EEPROM.put(0, pdata);
      EEPROM.commit();
      logMessage("Key: P-256 pubkey updated from config.h (key rotation)");
    }
  }
  // ─────────────────────────────────────────────────────────────────────────

  // Cache public key globally so event handlers can use it without touching EEPROM
  if (pdata.hasPubkey) {
    memcpy(g_serverPubKey, pdata.pubkey, 64);
    g_hasPubKey = true;
  }

  const char* wifiSsid;
  const char* wifiPass;

  if (pdata.magic == PROV_MAGIC && strlen(pdata.ssid) > 0) {
    wifiSsid = pdata.ssid;
    wifiPass = pdata.password;
    logMessage("WiFi: using provisioned SSID=" + String(wifiSsid));
    logMessage(pdata.hasPubkey ? "Key: P-256 public key present" : "WARN: no public key — messages cannot be verified");
  } else {
    wifiSsid = WIFI_SSID;
    wifiPass = WIFI_PASS;
    logMessage("WiFi: using hardcoded SSID=" + String(wifiSsid));
  }

  oledShowStatus("WiFi connecting...");
  WiFi.mode(WIFI_STA);
  WiFi.begin(wifiSsid, wifiPass);
  Serial.print("WiFi");
  for (int i = 0; i < 40 && WiFi.status() != WL_CONNECTED; i++) {
    delay(500); Serial.print(".");
    checkProvisionSerial();
  }
  if (WiFi.status() != WL_CONNECTED) {
    logMessage("ERR: WiFi failed — waiting for PROVISION via UART");
    oledShowStatus("WiFi FAILED!", "Send PROVISION", "via UART");
    while (true) { checkProvisionSerial(); delay(100); }
  }

  String ip = WiFi.localIP().toString();
  logMessage("OK: WiFi " + ip);

#if CFG_STATION_TYPE != 2
  display.clearDisplay(); display.setTextColor(WHITE); display.setTextSize(1);
  display.setCursor(0, 0); display.println("Ready! Log:");
  display.setTextSize(2); display.println(ip);
  display.setTextSize(1); display.println(""); display.println("Waiting for server...");
  display.display();
#endif

  logServer.on("/", handleRoot);
  logServer.on("/clear", handleClear);
  logServer.begin();
  logMessage("Log: http://" + ip);

  ws.onEvent(onWsEvent);
  ws.setReconnectInterval(5000);
  ws.enableHeartbeat(15000, 3000, 2);

  // ── Auto-connect to SignalR on boot ───────────────────────────────────────
  // Server address is baked into firmware via config.h — no /connect push needed.
  if (pdata.magic == PROV_MAGIC && strlen(pdata.server) > 0) {
    strncpy(g_wsHost, pdata.server, sizeof(g_wsHost) - 1);
    g_wsPort    = pdata.port;
    g_wsStarted = true;
    ws.begin(g_wsHost, g_wsPort, WS_PATH);
    logMessage("Auto-connecting to " + String(g_wsHost) + ":" + String(g_wsPort));
    oledShowStatus("Connecting...", g_wsHost);
  } else {
    logMessage("WARN: No server configured — re-flash with flashFirst=true");
    oledShowStatus("No server", "Re-flash device");
  }
  // ─────────────────────────────────────────────────────────────────────────

  logMessage("--- Ready ---");

  // ── Show provisioning status after Ready ──────────────────────────────────
  // Reads current EEPROM state so the user can confirm provisioning is intact
  // every time the device boots (visible in the web logger).
  {
    ProvData cur;
    EEPROM.get(0, cur);
    if (cur.magic == PROV_MAGIC) {
      if (cur.hasPubkey) {
        char hint[9] = {};
        for (int i = 0; i < 4; i++) sprintf(hint + 2*i, "%02x", cur.pubkey[i]);
        logMessage("PROVISION: server=" + String(cur.server) + ":" + String(cur.port)
                   + " pubkey=" + String(hint) + "...");
      } else {
        logMessage("PROVISION: server=" + String(cur.server) + ":" + String(cur.port)
                   + " pubkey=MISSING");
      }
    } else {
      logMessage("PROVISION: not set (hardcoded WiFi)");
    }
  }
  // ─────────────────────────────────────────────────────────────────────────
}

// ══════════════════════════════════════════════════
//  Loop
// ══════════════════════════════════════════════════
void loop() {
  checkProvisionSerial();
  if (g_wsStarted) ws.loop();
  logServer.handleClient();

  // Non-blocking relay auto-off
  if (g_relayOffAt != 0 && millis() >= g_relayOffAt) {
    relayAllOff(g_relayAddr, g_relayActiveLow);
    g_relayOffAt = 0;
    logMessage(">> Relay OFF (auto)");
  }

#if CFG_STATION_TYPE != 2
  char key = getKeyFromPCF8574();
  if (key != 0) {
    logMessage("Key: " + String(key));
    if (wsConnected) sendKey(key);
    else             logMessage("WARN: Not connected");
  }
#endif

  if (WiFi.status() != WL_CONNECTED) {
    logMessage("WiFi lost, reconnecting...");
    WiFi.reconnect();
    delay(1000);
  }
}
