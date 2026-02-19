// ═══════════════════════════════════════════════════
//  ESP8266 + SignalR + Keypad PCF8574 + SSD1306 OLED
//  لاگ: http://[IP_ESP]
//
//  کتابخانه‌ها:
//    1. WebSockets       by Markus Sattler
//    2. ArduinoJson      by Benoit Blanchon
//    3. Adafruit SSD1306
//    4. Adafruit GFX
//  (I2CKeyPad دیگه لازم نیست)
// ═══════════════════════════════════════════════════

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <WebSocketsClient.h>
#include <ArduinoJson.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

// ── WiFi ──────────────────────────────────────────
const char* WIFI_SSID = "$(M_2.4_M)$";
const char* WIFI_PASS = "M@ein#1383";

// ── SignalR Server ────────────────────────────────
const char* WS_HOST = "192.168.254.54";
const int   WS_PORT = 5000;
const char* WS_PATH = "/hardwareHub";

// ── I2C Pins (ESP-01) ─────────────────────────────
#define SDA_PIN 0   // GPIO0
#define SCL_PIN 2   // GPIO2

// ── OLED ──────────────────────────────────────────
#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
#define OLED_ADDR 0x3C
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);

// ══════════════════════════════════════════════════
//  Custom PCF8574 Keypad
//  R1-R4 → P0-P3 (بیت‌های پایین)
//  C1-C4 → P4-P7 (بیت‌های بالا)
// ══════════════════════════════════════════════════
#define KEYPAD_ADDR 0x20  // A0-A2 همه HIGH → 0x27

const char KEYMAP[4][4] = {
  {'1','2','3','A'},
  {'4','5','6','B'},
  {'7','8','9','C'},
  {'*','0','#','D'}
};

char getKeyFromPCF8574() {
  // R1-R4 → P0-P3, C1-C4 → P4-P7
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

        if (key != lastKey) {
          lastKey = key;
          return key;
        }
        return 0;
      }
    }
  }

  // هیچ کلیدی — بعد از 150ms واقعاً رها شدن، reset کن
  Wire.beginTransmission(KEYPAD_ADDR);
  Wire.write(0xFF);
  Wire.endTransmission();

  if (lastKey != 0) {
    if (releaseAt == 0) releaseAt = millis();
    if (millis() - releaseAt >= 150) {
      lastKey = 0;
      releaseAt = 0;
    }
  }
  return 0;
}

bool initKeypad() {
  Wire.beginTransmission(KEYPAD_ADDR);
  Wire.write(0xFF); // همه HIGH
  uint8_t err = Wire.endTransmission();
  return (err == 0);
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
  if (logs.length() > 8000)
    logs = logs.substring(4000);
}

void handleRoot() {
  String html = "<!DOCTYPE html><html><head>"
    "<meta charset='utf-8'>"
    "<meta http-equiv='refresh' content='2'>"
    "<style>"
    "body{background:#111;color:#0f0;font-family:monospace;padding:15px;}"
    "h2{color:#ff0;} a{color:#f80;}"
    ".box{background:#1a1a1a;padding:10px;border:1px solid #333;border-radius:5px;}"
    "</style></head><body>"
    "<h2>ESP8266 Log</h2>"
    "<a href='/clear'>Clear</a> | <a href='/'>Refresh</a>"
    "<hr><div class='box'>" + logs + "</div>"
    "</body></html>";
  logServer.send(200, "text/html", html);
}

void handleClear() {
  logs = "";
  logServer.sendHeader("Location", "/");
  logServer.send(302);
}

// ── OLED Helpers ──────────────────────────────────
void oledShowStatus(String line1, String line2 = "", String line3 = "") {
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
  Wire.beginTransmission(OLED_ADDR);
  Wire.write(0x00);
  Wire.write(0x21); Wire.write(0x00); Wire.write(0x7F);
  Wire.write(0x22); Wire.write(0x00); Wire.write(0x07);
  Wire.endTransmission();
  for (int i = 0; i < 1024; i += 16) {
    Wire.beginTransmission(OLED_ADDR);
    Wire.write(0x40);
    for (int j = 0; j < 16; j++) Wire.write(buf[i + j]);
    Wire.endTransmission();
  }
}

// ── Base64 Decode ─────────────────────────────────
static const char B64T[] =
  "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

int b64Decode(const char* in, uint8_t* out, int maxOut) {
  int n = 0, len = strlen(in);
  for (int i = 0; i + 3 < len && n + 2 < maxOut; i += 4) {
    uint8_t a = strchr(B64T, in[i])   - B64T;
    uint8_t b = strchr(B64T, in[i+1]) - B64T;
    uint8_t c = (in[i+2]!='=') ? (uint8_t)(strchr(B64T,in[i+2])-B64T) : 0;
    uint8_t d = (in[i+3]!='=') ? (uint8_t)(strchr(B64T,in[i+3])-B64T) : 0;
    if (n < maxOut) out[n++] = (a << 2) | (b >> 4);
    if (n < maxOut) out[n++] = (b << 4) | (c >> 2);
    if (n < maxOut) out[n++] = (c << 6) | d;
  }
  return n;
}

// ── SignalR ───────────────────────────────────────
void signalRHandshake() {
  String msg = "{\"protocol\":\"json\",\"version\":1}";
  msg += (char)0x1E;
  ws.sendTXT(msg);
  logMessage("SignalR: Handshake sent");
}

void sendKey(char key) {
  StaticJsonDocument<200> doc;
  doc["type"]   = 1;
  doc["target"] = "SendKey";
  doc.createNestedArray("arguments").add(String(key));
  String msg;
  serializeJson(doc, msg);
  msg += (char)0x1E;
  ws.sendTXT(msg);
  logMessage(">> Key sent: " + String(key));
}

void handleServerMessage(uint8_t* payload, size_t length) {
  String raw = String((char*)payload);
  int start = 0;
  while (start < (int)raw.length()) {
    int end = raw.indexOf((char)0x1E, start);
    if (end == -1) end = raw.length();
    String chunk = raw.substring(start, end);
    start = end + 1;
    if (chunk.length() < 2) continue;

    DynamicJsonDocument doc(2048);
    if (deserializeJson(doc, chunk) != DeserializationError::Ok) continue;

    int type = doc["type"] | 0;
    if (type == 6) continue;

    if (type == 1) {
      const char* target = doc["target"] | "";

      if (strcmp(target, "RenderDisplay") == 0) {
        const char* b64 = doc["arguments"][0];
        if (!b64) return;
        uint8_t* buf = (uint8_t*)malloc(1024);
        if (!buf) { logMessage("ERR: malloc failed"); return; }
        int n = b64Decode(b64, buf, 1024);
        if (n == 1024) {
          oledRenderBuffer(buf);
          logMessage(">> Display updated");
        } else {
          logMessage("ERR: decoded=" + String(n));
        }
        free(buf);
      }

      else if (strcmp(target, "PowerState") == 0) {
        bool on = doc["arguments"][0];
        Wire.beginTransmission(OLED_ADDR);
        Wire.write(0x00);
        Wire.write(on ? 0xAF : 0xAE);
        Wire.endTransmission();
        logMessage(">> Power: " + String(on ? "ON" : "OFF"));
      }
    }
  }
}

// ── WebSocket Events ──────────────────────────────
void onWsEvent(WStype_t type, uint8_t* payload, size_t length) {
  switch (type) {
    case WStype_CONNECTED:
      wsConnected = true;
      logMessage("WS: Connected");
      oledShowStatus("Connected!", WS_HOST);
      signalRHandshake();
      break;
    case WStype_DISCONNECTED:
      wsConnected = false;
      logMessage("WS: Disconnected");
      oledShowStatus("Disconnected", "Retrying...");
      break;
    case WStype_TEXT:
      handleServerMessage(payload, length);
      break;
    case WStype_ERROR:
      logMessage("WS: Error");
      break;
    default: break;
  }
}

// ══════════════════════════════════════════════════
//  Setup
// ══════════════════════════════════════════════════
void setup() {
  Serial.begin(115200);
  Wire.begin(SDA_PIN, SCL_PIN);

  // OLED
  if (!display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR)) {
    Serial.println("ERR: OLED");
    for(;;);
  }
  oledShowStatus("Booting...");
  logMessage("--- Boot ---");

  // Keypad
  if (initKeypad()) {
    logMessage("OK: Keypad at 0x" + String(KEYPAD_ADDR, HEX));
  } else {
    logMessage("ERR: Keypad not found at 0x" + String(KEYPAD_ADDR, HEX));
  }

  // WiFi
  oledShowStatus("WiFi connecting...");
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  Serial.print("WiFi");
  for (int i = 0; i < 40 && WiFi.status() != WL_CONNECTED; i++) {
    delay(500); Serial.print(".");
  }
  if (WiFi.status() != WL_CONNECTED) {
    logMessage("ERR: WiFi failed");
    oledShowStatus("WiFi FAILED!", "Restarting...");
    delay(2000);
    ESP.restart();
  }

  String ip = WiFi.localIP().toString();
  logMessage("OK: WiFi " + ip);

  display.clearDisplay();
  display.setTextColor(WHITE);
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.println("Connected!");
  display.println("Log:");
  display.setTextSize(2);
  display.println(ip);
  display.display();

  logServer.on("/", handleRoot);
  logServer.on("/clear", handleClear);
  logServer.begin();
  logMessage("Log: http://" + ip);

  ws.begin(WS_HOST, WS_PORT, WS_PATH);
  ws.onEvent(onWsEvent);
  ws.setReconnectInterval(3000);
  ws.enableHeartbeat(15000, 3000, 2);

  logMessage("--- Ready ---");
}

// ══════════════════════════════════════════════════
//  Loop
// ══════════════════════════════════════════════════
void loop() {
  ws.loop();
  logServer.handleClient();

  char key = getKeyFromPCF8574();
  if (key != 0) {
    logMessage("Key: " + String(key));
    if (wsConnected) {
      sendKey(key);
    } else {
      logMessage("WARN: Not connected");
    }
  }

  if (WiFi.status() != WL_CONNECTED) {
    logMessage("WiFi lost, reconnecting...");
    WiFi.reconnect();
    delay(1000);
  }
}
