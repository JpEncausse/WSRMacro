using System;
using System.IO;
using System.Web;
using System.Xml.XPath;
using System.Collections.Generic;
using Pitch;
#if MICRO
using System.Speech;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
#endif

#if KINECT
using Microsoft.Speech;
using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;
#endif

namespace net.encausse.sarah {
  public class WSRSpeechEngine {

    protected SpeechRecognitionEngine engine = null;
    public SpeechRecognitionEngine GetEngine() {
      return engine;
    }

    public String Name       { get; set; } 
    public double Confidence { get; set; }

    protected WSRConfig cfg;

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    public WSRSpeechEngine(String name, String language, double confidence) {
      this.Name = name;
      this.Confidence = confidence;
      this.cfg = WSRConfig.GetInstance();
      this.engine = new SpeechRecognitionEngine(new System.Globalization.CultureInfo(language));
    }

    public void Init() {

      cfg.logInfo("ENGINE - " + Name, "Init recognizer");
      
      engine.SpeechRecognized   += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);
      engine.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);
      engine.AudioStateChanged  += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);
      engine.SpeechHypothesized += new EventHandler<SpeechHypothesizedEventArgs>(recognizer_SpeechHypothesized);
      engine.SpeechDetected     += new EventHandler<SpeechDetectedEventArgs>(recognizer_SpeechDetected);
      
      engine.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", (int) (this.Confidence * 100));

      engine.MaxAlternates              = cfg.MaxAlternates;
      engine.InitialSilenceTimeout      = cfg.InitialSilenceTimeout;
      engine.BabbleTimeout              = cfg.BabbleTimeout;
      engine.EndSilenceTimeout          = cfg.EndSilenceTimeout;
      engine.EndSilenceTimeoutAmbiguous = cfg.EndSilenceTimeoutAmbiguous;

      if (!cfg.Adaptation) {
        engine.UpdateRecognizerSetting("AdaptationOn", 0);
      }

      cfg.logInfo("ENGINE - " + Name, "AudioLevel: "                 + engine.AudioLevel);
      cfg.logInfo("ENGINE - " + Name, "MaxAlternates: "              + engine.MaxAlternates);
      cfg.logInfo("ENGINE - " + Name, "BabbleTimeout: "              + engine.BabbleTimeout);
      cfg.logInfo("ENGINE - " + Name, "InitialSilenceTimeout: "      + engine.InitialSilenceTimeout);
      cfg.logInfo("ENGINE - " + Name, "EndSilenceTimeout: "          + engine.EndSilenceTimeout);
      cfg.logInfo("ENGINE - " + Name, "EndSilenceTimeoutAmbiguous: " + engine.EndSilenceTimeoutAmbiguous);

      tracker = new PitchTracker();
      tracker.SampleRate = 16000.0f;
      tracker.PitchDetected += OnPitchDetected;
    }

    private DateTime loading = DateTime.MinValue;
    public void LoadGrammar() {
      if (this.Name == "RTP") { return; }
      var cache = WSRSpeechManager.GetInstance().Cache;
      foreach (WSRSpeecGrammar g in cache.Values) {
        if (g.LastModified < loading) { continue; }
        cfg.logInfo("GRAMMAR", Name + ": Load Grammar to Engine");
        g.LoadGrammar(this);
      }
      loading = DateTime.Now;
    }

    // ------------------------------------------
    //  PITCH TRACKER
    // ------------------------------------------

    PitchTracker tracker = null;
    List<double> pitch = new List<double>();
    private void OnPitchDetected(PitchTracker sender, PitchTracker.PitchRecord pitchRecord) {
      // During the call to PitchTracker.ProcessBuffer, this event will be fired zero or more times,
      // depending how many pitch records will fit in the new and previously cached buffer.
      //
      // This means that there is no size restriction on the buffer that is passed into ProcessBuffer.
      // For instance, ProcessBuffer can be called with one large buffer that contains all of the
      // audio to be processed, or just a small buffer at a time which is more typical for realtime
      // applications. This PitchDetected event will only occur once enough data has been accumulated
      // to do another detect operation.
      /*
      cfg.logInfo("PITCH", "MidiCents: " + pitchRecord.MidiCents
                         + " MidiNote: " + pitchRecord.MidiNote
                         + " Pitch: " + pitchRecord.Pitch
                         + " RecordIndex: " + pitchRecord.RecordIndex);*/
      double d = pitchRecord.Pitch;
      if (d > 0) { pitch.Add(d); }
    }

    protected void TrackPitch(RecognitionResult rr) {
      if (rr.Audio == null) { return;  }
      using (MemoryStream audioStream = new MemoryStream()) {
        rr.Audio.WriteToWaveStream(audioStream);
        audioStream.Position = 0;

        byte[] audioBytes = audioStream.ToArray();
        float[] audioBuffer = new float[audioBytes.Length / 2];
        for (int i = 0, j = 0; i < audioBytes.Length / 2; i += 2, j++) {

          // convert two bytes to one short
          short s = BitConverter.ToInt16(audioBytes, i);

          // convert to range from -1 to (just below) 1
          audioBuffer[j] = s / 32768.0f;
        }

        // Reset
        tracker.Reset(); 
        pitch.Clear();

        // Process
        tracker.ProcessBuffer(audioBuffer);

        // Notify
        WSRProfileManager.GetInstance().UpdatePitch(pitch.Mean());
      }
    }

    // ==========================================
    //  VIRTUAL
    // ==========================================

    public virtual void HandleRequest(String url, String path) {
      if (null != path) {
        WSRHttpManager.GetInstance().SendUpoad(url, path);
      }
      else {
        WSRHttpManager.GetInstance().SendRequest(url);
      }
    }

    // ==========================================
    //  RECOGNIZE
    // ==========================================

    protected void SpeechRecognized(RecognitionResult rr) { 

      // 1. Prevent while speaking
      if (WSRSpeakerManager.GetInstance().Speaking) {
        cfg.logWarning("ENGINE - " + Name, "REJECTED Speech while speaking : " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      // 2. Prevent while working
      XPathNavigator xnav = HandleSpeech(rr);
      if (xnav == null) {
        return;
      }

      // 3. Reset context timeout
      if (rr.Grammar.Name != "Dyn") {
        WSRSpeechManager.GetInstance().ResetContextTimeout();
      }
      else {
        cfg.logInfo("ENGINE - " + Name, "DYN reset to default context");
        WSRSpeechManager.GetInstance().SetContext("default");
        WSRSpeechManager.GetInstance().ForwardContext();
      }

      // 4. Track Audio Pitch
      TrackPitch(rr);

      // 5. Reset Speech Buffer
      WSRSpeakerManager.GetInstance().SpeechBuffer = "";

      // 6. Hook
      String path = cfg.WSR.HandleCustomAttributes(xnav);

      // 7. Parse Result's URL
      String url = GetURL(xnav, rr.Confidence);

      // 8. Parse Result's Dication
      url = HandleWildcard(rr, url);
      if (url == null) { return;  }

      // 9. Send the request
      HandleRequest(url, path);
    }

    protected String GetURL(XPathNavigator xnav, float confidence) {
      XPathNavigator xurl = xnav.SelectSingleNode("/SML/action/@uri");
      if (xurl == null) { return null; }

      // Build URI
      String url = xurl.Value;

      // Append ? and & for append params in URL
      url += url.IndexOf("?") > 0 ? "&" : "?";

      // Build QueryString
      url += GetQueryString(xnav.Select("/SML/action/*"));

      // Append Confidence
      url += "confidence=" + ("" + confidence).Replace(",", ".") + "&";

      return url;
    }

    protected String GetQueryString(XPathNodeIterator it) {
      String qs = "";
      while (it.MoveNext()) {
        String children = "";
        if (it.Current.Name == "confidence")
          continue;
        if (it.Current.Name == "uri")
          continue;
        if (it.Current.HasChildren) {
          children = GetQueryString(it.Current.SelectChildren(String.Empty, it.Current.NamespaceURI));
        }
        qs += (children == "") ? (it.Current.Name + "=" + it.Current.Value + "&") : (children);
      }
      return qs;
    }

    protected double GetConfidence(XPathNavigator xnav) {
      XPathNavigator level = xnav.SelectSingleNode("/SML/action/@threashold");
      if (level != null) {
        cfg.logInfo("HTTP - " + Name, "Using confidence level: " + level.Value);
        return level.ValueAsDouble;
      }
      return Confidence;
    }

    protected XPathNavigator HandleSpeech(RecognitionResult rr) {
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();

      double confidence = GetConfidence(xnav);
      if (rr.Confidence < confidence) {
        cfg.logWarning("ENGINE - "+Name, "REJECTED Speech: " + rr.Confidence + " < " + confidence + " Device: " + " Text: " + rr.Text);
        return null;
      }

      if (rr.Words[0].Confidence < cfg.trigger) {
        cfg.logWarning("ENGINE - "+Name, "REJECTED Trigger: " + rr.Words[0].Confidence + " Text: " + rr.Words[0].Text);
        return null;
      }

      cfg.logWarning("ENGINE - "+Name, "RECOGNIZED Speech: " + rr.Confidence + " / " + rr.Words[0].Confidence + " (" + rr.Words[0].Text + ")" + " Device: " + " Text: " + rr.Text);
      cfg.logDebug("ENGINE - "+Name, xnav.OuterXml);

      if (cfg.DEBUG) { WSRSpeechManager.GetInstance().DumpAudio(rr); } 

      return xnav;
    }

    protected String HandleWildcard(RecognitionResult rr, String url) {
      if (rr.Audio == null) { return url;  }

      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
      XPathNavigator wildcard = xnav.SelectSingleNode("/SML/action/@dictation");
      if (wildcard == null) { return url; }

      // Retrieve language
      String language = cfg.language;
      if (wildcard.Value != "true") {
        language = wildcard.Value;
      }

      // Google
      using (MemoryStream audioStream = new MemoryStream()) {
        rr.Audio.WriteToWaveStream(audioStream);
        // rr.GetAudioForWordRange(rr.Words[word], rr.Words[word]).WriteToWaveStream(audioStream);
        audioStream.Position = 0;
        var speech2text = WSRSpeechManager.GetInstance().ProcessAudioStream(audioStream, language);
        if (url != null) {
          url += "dictation=" + HttpUtility.UrlEncode(speech2text);
        }
      }

      return url;
    }

    // ==========================================
    //  CALLBACK
    // ==========================================

    protected bool IsWorking { get; set; }
    protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
      RecognitionResult rr = e.Result;

      if (!WSRSpeechManager.GetInstance().Listening) {
        cfg.logInfo("ENGINE - " + Name, "REJECTED not listening");
        return;
      }

      if (IsWorking) {
        cfg.logInfo("ENGINE - " + Name, "REJECTED Speech while working: " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      var start = DateTime.Now;
      IsWorking = true;

      try {
        SpeechRecognized(rr);
      } 
      catch(Exception ex){
        cfg.logError("ENGINE - " + Name, ex);
      }

      IsWorking = false;
      var stop = DateTime.Now;
      cfg.logInfo("ENGINE - " + Name, "SpeechRecognized: " + (stop - start).TotalMilliseconds + "ms Text: "+ rr.Text);
    }

    protected void recognizer_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e) {
      String resultText = e.Result != null ? e.Result.Text : "<null>";
      cfg.logInfo("ENGINE - " + Name, "RecognizeCompleted (" + DateTime.Now.ToString("mm:ss.f") + "): " + resultText);
      cfg.logDebug("ENGINE - " + Name, "BabbleTimeout: " + e.BabbleTimeout + "; InitialSilenceTimeout: " + e.InitialSilenceTimeout + "; Result text: " + resultText);

      // StartRecognizer();
    }
    protected void recognizer_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
      cfg.logDebug("ENGINE - " + Name, "AudioStateChanged (" + DateTime.Now.ToString("mm:ss.f") + "):" + e.AudioState);
    }
    protected void recognizer_SpeechHypothesized(object sender, SpeechHypothesizedEventArgs e) {
      cfg.logDebug("ENGINE - " + Name, "recognizer_SpeechHypothesized " + e.Result.Text + " => " + e.Result.Confidence);
      
    }
    protected void recognizer_SpeechDetected(object sender, SpeechDetectedEventArgs e) {
      cfg.logDebug("ENGINE - " + Name, "recognizer_SpeechDetected");
    }

    // ==========================================
    //  START / STOP
    // ==========================================

    public void Start() {
      try {
        engine.RecognizeAsync(RecognizeMode.Multiple);
        cfg.logInfo("ENGINE - " + Name, "Start listening");
      }
      catch (Exception ex) {
        cfg.logError("ENGINE - " + Name, "No device found");
        cfg.logError("ENGINE - " + Name, ex);
      }
    }

    public void Stop(bool dispose) {
      engine.RecognizeAsyncStop();
      if (dispose) { engine.Dispose(); }
      cfg.logInfo("ENGINE - " + Name, "Stop listening...done");
    }
  }
}
