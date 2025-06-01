const int NUM_SLIDERS = 5;
const int analogInputs[NUM_SLIDERS] = {A0, A1, A2, A3, A4};

// Hier speichern wir die aktuellen und vorherigen Werte
int analogSliderValues[NUM_SLIDERS];
int prevSliderValues[NUM_SLIDERS];

// Ab hier: alles unterhalb dieses Schwellenwerts wird als „kein echter Wechsel“ ignoriert
const int THRESHOLD = 5; 

int serialbaud = 9600;

unsigned long lastRead = 0;
const unsigned long interval = 100;

void setup() { 
  for (int i = 0; i < NUM_SLIDERS; i++) {
    pinMode(analogInputs[i], INPUT);
    prevSliderValues[i] = -1;               // initial ungleich jedem echten Wert
  }

  Serial.begin(serialbaud);
}

void loop() {
  // serial is searched for media-info
  if(Serial.available()){
    Serial.println("available");
    readSerial();
  }
  // checking, processing and sending slider-values only if time-interval is exceeded
  if(millis()-lastRead>=interval){
    lastRead=millis();
    // new readings are pulled
    updateSliderValues();
    
    // checking if one of the sliders changed more than THRESHOLD
    bool anyChanged = false;
    for (int i = 0; i < NUM_SLIDERS; i++) {
      if (prevSliderValues[i] < 0 ||              // beim ersten Mal gilt „changed“
          abs(analogSliderValues[i] - prevSliderValues[i]) > THRESHOLD) {
        anyChanged = true;
        break;
      }
    }

    if (anyChanged) {
      sendSliderValues();

      // Jetzt die aktuellen Werte als „neu vorherig“ übernehmen
      for (int i = 0; i < NUM_SLIDERS; i++) {
        prevSliderValues[i] = analogSliderValues[i];
      }
    }
  }

}

void updateSliderValues() {
  for (int i = 0; i < NUM_SLIDERS; i++) {
    int raw = analogRead(analogInputs[i]);
    // Du hattest vorher: abs(analogRead(...) - 1023.0)
    // Das invertiert ja nur; hier übernehmen wir das beibehalten:
    analogSliderValues[i] = abs(raw - 1023);
  }
}

void sendSliderValues() {
  // Baut den String "W1|W2|W3|W4|W5" auf und sendet per Serial.println
  String builtString;
  for (int i = 0; i < NUM_SLIDERS; i++) {
    builtString += String(analogSliderValues[i]);
    if (i < NUM_SLIDERS - 1) {
      builtString += "|";
    }
  }
  Serial.println(builtString);
}

void readSerial(){
  String input = Serial.readStringUntil("\n");
  Serial.print("(Ar)Empfangen: "+input);
}
