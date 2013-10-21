using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;


namespace net.encausse.sarah {


  // ==========================================
  //  MANAGER
  // ==========================================

  public class WSRSpeakerManager {

    // Singleton
    private static WSRSpeakerManager manager;
    public static WSRSpeakerManager GetInstance() {
      if (manager == null) {
        manager = new WSRSpeakerManager();
      }
      return manager;
    }

    // Constructor
    public WSRSpeakerManager(){

      // Enumerate all Speaker
      if ("all" == WSRConfig.GetInstance().Speakers) {
        int waveOutDevices = WaveOut.DeviceCount;
        for (int n = 0; n < waveOutDevices; n++) {

          var cap = WaveOut.GetCapabilities(n);
          WSRConfig.GetInstance().logInfo("TTS", "Add Speaker Device: " + n
                                               + " Product: "  + cap.ProductName
                                               + " Channels: " + cap.Channels
                                               + " Playback: " + cap.SupportsPlaybackRateControl);
          speakers.Add(new WSRSpeaker(n));
        }
        return;
      }

      // Enumerate declared Speaker
      foreach (var spkr in WSRConfig.GetInstance().Speakers.Split(',')) {
        var idx = int.Parse(spkr);
        var cap = WaveOut.GetCapabilities(idx);
        WSRConfig.GetInstance().logInfo("TTS", "Add Speaker Device: " + idx
                                              + " Product: " + cap.ProductName
                                              + " Channels: " + cap.Channels
                                              + " Playback: " + cap.SupportsPlaybackRateControl);
        speakers.Add(new WSRSpeaker(idx));
      }
    }

    public bool Speak(String tts, bool async) {

      var name = WSRProfileManager.GetInstance().CurrentName();
      tts = Regex.Replace(tts, "\\[name\\]", name, RegexOptions.IgnoreCase);

      Task last = null;
      foreach (var speaker in speakers) {
        last = Task.Factory.StartNew(() => speaker.Speak(tts, async));
      }
      if (!async && null != last) { last.Wait(); }
      return true;
    }

    public void ShutUp() {
      foreach (var speaker in speakers) {
        speaker.ShutUp();
      }
    }

    public bool Play(string fileName) {
      foreach (var speaker in speakers) {
        Task.Factory.StartNew(() => speaker.Play(fileName));
      }
      return true;
    }

    public bool Stream(string url) {
      foreach (var speaker in speakers) {
        Task.Factory.StartNew(() => speaker.Stream(url));
      }
      return true;
    }

    public bool Stop(string fileName) {
      foreach (var speaker in speakers) {
        speaker.Stop(fileName);
      }
      return true;
    }

    public void Dispose() {
      foreach (var speaker in speakers) {
        speaker.Dispose();
      }
    }

    private List<WSRSpeaker> speakers = new List<WSRSpeaker>();
    public bool Speaking {
      get {
        bool speak = false;
        foreach (var speaker in speakers) {
          speak = speak || speaker.speaking;
        }
        return speak;
      }
      set { }
    }
  }

  // ==========================================
  //  SPEAKER
  // ==========================================
  
  public class WSRSpeaker {

    private int device;
    private WaveOut WaveOutSpeech;
    private SpeechSynthesizer synthesizer;
    

    // ------------------------------------------
    //  CONSTRUCTOR
    // ------------------------------------------

    public WSRSpeaker(int device) {
      this.device = device;

      this.WaveOutSpeech = new WaveOut(WaveCallbackInfo.FunctionCallback());
      this.WaveOutSpeech.DeviceNumber = device;

      this.synthesizer = new SpeechSynthesizer();
      this.synthesizer.SpeakCompleted += new EventHandler<SpeakCompletedEventArgs>(synthesizer_SpeakCompleted);
      
      // Enumerate Voices
      foreach (InstalledVoice voice in synthesizer.GetInstalledVoices()) {
        VoiceInfo info = voice.VoiceInfo;
        WSRConfig.GetInstance().logInfo("TTS", "[" + device + "]" + "Name: " + info.Name + " Culture: " + info.Culture);
      }
      
      // Select Voice
      String v = WSRConfig.GetInstance().voice;
      if (v != null && v.Trim() != "") {
        WSRConfig.GetInstance().logInfo("TTS", "[" + device + "]" + "Select voice: " + v);
        synthesizer.SelectVoice(v);
      }
    }

    public void Dispose() {
      WaveOutSpeech.Dispose();
    }

    private void RunSession(WaveOut WaveOut, WaveStream stream, string match) {
      played.Add(match);
      WSRConfig.GetInstance().logInfo("PLAYER", "[" + device + "]" + "Start Player");
      Stopwatch timer = new Stopwatch(); timer.Start();
      using (WaveChannel32 volumeStream = new WaveChannel32(stream)) {
        volumeStream.Volume = match == "TTS" ? WSRConfig.GetInstance().SpkVolTTS / 100f : WSRConfig.GetInstance().SpkVolPlay / 100f;
        WaveOut.Init(volumeStream);
        WaveOut.Play();
        while (stream.CurrentTime < stream.TotalTime && played.Contains(match) && timer.ElapsedMilliseconds < 1000 * 60 * 2) {
          Thread.Sleep(100);
        }
        WaveOut.Stop();
        stream.Dispose();
      }

      WSRConfig.GetInstance().logInfo("PLAYER", "[" + device + "]" + "End Player");
      played.Remove(match);
    }

    // ==========================================
    //  SPEECH
    // ==========================================

    public bool speaking = false;
    public void Speak(String tts, bool async) {

      if (tts == null) { return; }
      if (speaking)    { return; }

      WSRConfig.GetInstance().logInfo("TTS", "[" + device + "] " + "Say: " + tts);
      speaking = true;
      try {
        // Build and speak a prompt.
        PromptBuilder builder = new PromptBuilder();
        builder.AppendText(tts);

        // Setup buffer
        using (var ms = new MemoryStream()) {
          synthesizer.SetOutputToWaveStream(ms);
          synthesizer.Speak(builder); // Synchronous
          ms.Position = 0;
          if (ms.Length > 0) {
            RunSession(WaveOutSpeech, new WaveFileReader(ms), "TTS");
          }
        }
      }
      catch (Exception ex) {
        WSRConfig.GetInstance().logError("TTS", ex);
      }
      speaking = false;
    }

    public void ShutUp() {
      Stop("TTS");
    }

    protected void synthesizer_SpeakCompleted(object sender, SpeakCompletedEventArgs e) { }

    // ==========================================
    //  PLAYER
    // ==========================================

    List<String> played = new List<String>();
    public bool Stop(string key) {
      if (key == null) { return false; }
      played.Remove(key);
      return true;
    }

    public void Play(string fileName) {
      if (fileName == null) { return; }
      if (fileName.StartsWith("http")) {
        Stream(fileName); return;
      }
      if (!File.Exists(fileName)) { return; }

      // Play WaveStream
      bool wav = fileName.ToLower().EndsWith(".wav") || fileName.ToLower().EndsWith(".wma");
      var reader = wav ? (WaveStream)new WaveFileReader(fileName)
                       : (WaveStream)new Mp3FileReader(fileName);

      WaveOut WaveOutPlay = new WaveOut(WaveCallbackInfo.FunctionCallback());
      WaveOutPlay.DeviceNumber = device;
      RunSession(WaveOutPlay, reader, fileName);
      WaveOutPlay.Dispose();
    }

    public void Stream(string url) {
      if (url == null) { return; }

      WaveOut WaveOutPlay = new WaveOut(WaveCallbackInfo.FunctionCallback());
      WaveOutPlay.DeviceNumber = device;

      using (var ms = new MemoryStream())
      using (var stream = WebRequest.Create(url).GetResponse().GetResponseStream()) {
        byte[] buffer = new byte[32768]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
          ms.Write(buffer, 0, read);
        }
        ms.Position = 0;

        // Play WaveStream
        bool wav = url.ToLower().EndsWith(".wav") || url.ToLower().EndsWith(".wma");
        var reader = wav ? (WaveStream)new WaveFileReader(ms)
                         : (WaveStream)new Mp3FileReader(ms);
        
        RunSession(WaveOutPlay, reader, url);
      }

      WaveOutPlay.Dispose();
    }
  }
}
