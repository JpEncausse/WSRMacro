using System;
using System.IO;
using System.Web;
using System.Xml.XPath;
using System.Collections.Generic;

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

    public WSRSpeechEngine(String name, double confidence) {
      this.Name = name;
      this.Confidence = confidence;
      this.cfg = WSRConfig.GetInstance();
    }

    public void Init(String language) {

      cfg.logInfo("ENGINE", Name + ": Init recognizer: " + language);

      engine = new SpeechRecognitionEngine(new System.Globalization.CultureInfo(language));
      engine.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);
      engine.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);
      engine.AudioStateChanged += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);
      engine.SpeechHypothesized += new EventHandler<SpeechHypothesizedEventArgs>(recognizer_SpeechHypothesized);
      engine.SpeechDetected += new EventHandler<SpeechDetectedEventArgs>(recognizer_SpeechDetected);

      engine.MaxAlternates = 2;
      // engine.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
      // engine.BabbleTimeout = TimeSpan.FromSeconds(2);
      // engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);
      // engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

      if (!cfg.adaptation) {
        engine.UpdateRecognizerSetting("AdaptationOn", 0);
      }

      cfg.logDebug("ENGINE", Name + ": MaxAlternates: " + engine.MaxAlternates);
      cfg.logDebug("ENGINE", Name + ": BabbleTimeout: " + engine.BabbleTimeout);
      cfg.logDebug("ENGINE", Name + ": InitialSilenceTimeout: " + engine.InitialSilenceTimeout);
      cfg.logDebug("ENGINE", Name + ": EndSilenceTimeout: " + engine.EndSilenceTimeout);
      cfg.logDebug("ENGINE", Name + ": EndSilenceTimeoutAmbiguous: " + engine.EndSilenceTimeoutAmbiguous);
    }

    public void LoadGrammar() {
      cfg.logInfo("GRAMMAR", Name + ":Load Grammar to Engine");
      WSRSpeech.GetInstance().LoadGrammar(engine);
    }

    // ==========================================
    //  VIRTUAL
    // ==========================================


    public virtual void HandleRequest(String url, String path) {
      WSRHttpManager.GetInstance().SendRequest(url, path);
    }

    // ==========================================
    //  RECOGNIZE
    // ==========================================

    protected void SpeechRecognized(RecognitionResult rr) {

      // 1. Prevent while speaking
      if (WSRSpeaker.GetInstance().IsSpeaking()) {
        cfg.logInfo("ENGINE", "REJECTED Speech while speaking: " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      // 2. Prevent while working
      XPathNavigator xnav = HandleSpeech(rr);
      if (xnav == null) {
        return;
      }

      // 3. Reset context timeout
      WSRSpeech.GetInstance().ResetContextTimeout();

      // 4. Hook
      String path = cfg.GetWSRMicro().HandleCustomAttributes(xnav);

      // 5. Parse Result's URL
      String url = GetURL(xnav, rr.Confidence);

      // 6. Parse Result's Dication
      url = HandleWildcard(rr, url);
      if (url == null) { return;  }

      // 7. Send the request
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

      // Append Directory Path
      url += "directory=" + WSRSpeech.GetInstance().GetGrammarPath();

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
        cfg.logInfo("HTTP", "Using confidence level: " + level.Value);
        return level.ValueAsDouble;
      }
      return Confidence;
    }

    protected XPathNavigator HandleSpeech(RecognitionResult rr) {
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();

      double confidence = GetConfidence(xnav);
      if (rr.Confidence < confidence) {
        cfg.logInfo("ENGINE", "REJECTED Speech: " + rr.Confidence + " < " + confidence + " Device: " + WSRSpeech.GetInstance().GetDeviceInfo(this) + " Text: " + rr.Text);
        return null;
      }

      if (rr.Words[0].Confidence < cfg.trigger) {
        cfg.logInfo("ENGINE", "REJECTED Trigger: " + rr.Words[0].Confidence + " Text: " + rr.Words[0].Text);
        return null;
      }

      cfg.logInfo("ENGINE", "RECOGNIZED Speech: " + rr.Confidence + "/" + rr.Words[0].Confidence + " Device: " + WSRSpeech.GetInstance().GetDeviceInfo(this) + " Text: " + rr.Text);
      cfg.logDebug("ENGINE", xnav.OuterXml);

      if (cfg.DEBUG) { WSRSpeech.GetInstance().DumpAudio(rr); } 

      return xnav;
    }

    protected String HandleWildcard(RecognitionResult rr, String url) {

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
        var speech2text = WSRSpeech.GetInstance().ProcessAudioStream(audioStream, language);
        if (url != null) {
          url += "&dictation=" + HttpUtility.UrlEncode(speech2text);
        }
      }

      return url;
    }

    // ==========================================
    //  CALLBACK
    // ==========================================

    protected bool IsWorking = false;
    protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
      RecognitionResult rr = e.Result;

      if (IsWorking) {
        cfg.logInfo("ENGINE", "REJECTED Speech while working: " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      var start = DateTime.Now;
      IsWorking = true;

      try {
        SpeechRecognized(rr);
      } 
      catch(Exception ex){
        cfg.logError("ENGINE", ex);
      }

      IsWorking = false;
      var stop = DateTime.Now;
      cfg.logInfo("ENGINE", "SpeechRecognized: " + (stop - start).TotalMilliseconds + "ms");
    }

    protected void recognizer_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e) {
      String resultText = e.Result != null ? e.Result.Text : "<null>";
      cfg.logInfo( "ENGINE", Name + ": RecognizeCompleted (" + DateTime.Now.ToString("mm:ss.f") + "): " + resultText);
      cfg.logDebug("ENGINE", Name + ": BabbleTimeout: " + e.BabbleTimeout + "; InitialSilenceTimeout: " + e.InitialSilenceTimeout + "; Result text: " + resultText);

      // StartRecognizer();
    }
    protected void recognizer_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
      cfg.logDebug("ENGINE", Name + ": AudioStateChanged (" + DateTime.Now.ToString("mm:ss.f") + "):" + e.AudioState);
    }
    protected void recognizer_SpeechHypothesized(object sender, SpeechHypothesizedEventArgs e) {
      cfg.logDebug("ENGINE", Name + ": recognizer_SpeechHypothesized");
    }
    protected void recognizer_SpeechDetected(object sender, SpeechDetectedEventArgs e) {
      cfg.logDebug("ENGINE", Name + ": recognizer_SpeechDetected");
    }

    // ==========================================
    //  START / STOP
    // ==========================================

    public void Start() {
      try {
        engine.RecognizeAsync(RecognizeMode.Multiple);
        cfg.logInfo("ENGINE", Name + ": Start listening");
      }
      catch (Exception ex) {
        cfg.logError("ENGINE", Name + ": No device found");
        cfg.logError("ENGINE", ex);
      }
    }

    public void Stop() {
      engine.RecognizeAsyncStop();
      engine.Dispose();
      cfg.logInfo("ENGINE", Name + ": Stop listening...done");
    }
  }
}
