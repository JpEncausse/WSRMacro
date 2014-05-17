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

    public String SpeechBuffer = "";
    private Queue<string> queue = new Queue<String>();
    
    private bool working = false;
    private String Lock = "LOCK";
    public bool Speak(String tts, bool async) {
      
      // Run Async
      if (async) { return SpeakAsync(tts); }

      // Compute name
      var name = WSRProfileManager.GetInstance().CurrentName();
      tts = Regex.Replace(tts, "\\[name\\]", name, RegexOptions.IgnoreCase);

      WSRConfig.GetInstance().logInfo("TTS", "Try speaking ... " + tts);

      lock (Lock) { 
        // Prevent multiple Speaking
        if (Speaking || working) {
          WSRConfig.GetInstance().logInfo("TTS", "Already speaking ... enqueue");
          queue.Enqueue(tts); return false;
        }
        working = true;
      }

      // Run all speech in a list of Task
      List<Task> latest = new List<Task>();
      SpeechBuffer += tts + " ";
      foreach (var speaker in speakers) {
        latest.Add(Task.Factory.StartNew(() => speaker.Speak(tts)));
      }

      // Wait for end of Tasks
      foreach (var task in latest) { task.Wait(); }
      latest.Clear();
      lock (Lock) { working = false; }
      WSRConfig.GetInstance().logInfo("TTS", "End speaking");

      // Dequeue next (always async ...)
      if (queue.Count > 0) {
        Speak(queue.Dequeue(), true);
      }

      return true;
    }

    private bool SpeakAsync(String tts) {
      Task.Factory.StartNew(() => Speak(tts, false));
      return true;
    }

    public void ShutUp() {
      foreach (var speaker in speakers) {
        speaker.ShutUp();
      }
    }

    // ------------------------------------------
    //  PLAY
    // ------------------------------------------

    public bool Play(string fileName, bool async) {
      if (async) { return PlayAsync(fileName);  }

      List<Task> latest = new List<Task>();
      foreach (var speaker in speakers) {
        latest.Add(Task.Factory.StartNew(() => speaker.Play(fileName, async)));
      }
      foreach (var task in latest) { task.Wait(); }

      return true;
    }
    private bool PlayAsync(string fileName) {
      foreach (var speaker in speakers) {
        Task.Factory.StartNew(() => speaker.Play(fileName, true));
      }
      return true; 
    }

    public bool Stream(string url, bool async) {
      if (async) { return StreamAsync(url); }

      List<Task> latest = new List<Task>();
      foreach (var speaker in speakers) {
        latest.Add(Task.Factory.StartNew(() => speaker.Stream(url, async)));
      }
      foreach (var task in latest) { task.Wait(); }

      return true;
    }
    private bool StreamAsync(string url) {
      foreach (var speaker in speakers) {
        Task.Factory.StartNew(() => speaker.Stream(url, true));
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
        while (stream.CurrentTime < stream.TotalTime && played.Contains(match) && timer.ElapsedMilliseconds < 1000 * 60 * 8) {
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

    public  bool speaking = false;
    public void Speak(String tts) {

      if (tts == null) { return; }
      if (speaking) { return; } speaking = true;

      WSRConfig.GetInstance().logInfo("TTS", "[" + device + "] " + "Say: " + tts);
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

      WSRConfig.GetInstance().logInfo("PLAYER", "[" + device + "]" + "speaking false");
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

    public void Play(string fileName, bool async) {
      if (fileName == null) { return; }
      if (fileName.StartsWith("http")) {
        Stream(fileName, async);
        return;
      }
      if (!File.Exists(fileName)) { return; }

      // Play WaveStream
      bool wav = fileName.ToLower().EndsWith(".wav") || fileName.ToLower().EndsWith(".wma");
      var reader = wav ? WaveFormatConversionStream.CreatePcmStream(new WaveFileReader(fileName))
                       : (WaveStream)new Mp3FileReader(fileName);

      WaveOut WaveOutPlay = new WaveOut(WaveCallbackInfo.FunctionCallback());
      WaveOutPlay.DeviceNumber = device;
      RunSession(WaveOutPlay, reader, fileName);
      WaveOutPlay.Dispose();
    }

    public void Stream(string url, bool async) {
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
        var reader = wav ? WaveFormatConversionStream.CreatePcmStream(new WaveFileReader(ms))
                         : (WaveStream)new Mp3FileReader(ms);

        RunSession(WaveOutPlay, reader, url);
      }

      WaveOutPlay.Dispose();
    }
  }
}
