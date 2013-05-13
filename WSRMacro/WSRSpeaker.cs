using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Speech.Synthesis;
using NAudio.Wave;

namespace net.encausse.sarah {

  public class WSRSpeaker {

    // Singleton
    private static WSRSpeaker manager;
    public static WSRSpeaker GetInstance() {
      if (manager == null) {
        manager = new WSRSpeaker();
      }
      return manager;
    }

    private bool speaking = false;
    public bool IsSpeaking() {
      return speaking;
    }

    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    protected WSRSpeaker() {

      // Build synthesizer
      synthesizer = new SpeechSynthesizer();
      synthesizer.SetOutputToDefaultAudioDevice();
      synthesizer.SpeakCompleted += new EventHandler<SpeakCompletedEventArgs>(synthesizer_SpeakCompleted);

      foreach (InstalledVoice voice in synthesizer.GetInstalledVoices()) {
        VoiceInfo info = voice.VoiceInfo;
        WSRConfig.GetInstance().logInfo("TTS", "Name: " + info.Name + " Culture: " + info.Culture);
      }

      String v = WSRConfig.GetInstance().voice;
      if (v != null && v.Trim() != "") {
        WSRConfig.GetInstance().logInfo("TTS", "Select voice: " + v);
        synthesizer.SelectVoice(v);
      }
      
    }

    // ==========================================
    //  WSRMacro SPEECH
    // ==========================================

    protected SpeechSynthesizer synthesizer = null;
    public bool Speak(String tts) {

      if (tts == null) { return false; }

      WSRConfig.GetInstance().logInfo("TTS", "Say: " + tts);
      speaking = true;

      // Build and speak a prompt.
      PromptBuilder builder = new PromptBuilder();
      builder.AppendText(tts);
      synthesizer.SpeakAsync(builder);

      return true;
    }

    protected void synthesizer_SpeakCompleted(object sender, SpeakCompletedEventArgs e) {
      speaking = false;
    }

    // ==========================================
    //  WSRMacro PLAYER
    // ==========================================

    List<String> played = new List<String>();

    public bool Stop(string key) {
      if (key == null) { return false; }
      played.Remove(key);
      return true;
    }


    public bool Play(string fileName) {

      if (fileName == null) { return false; }
      if (fileName.StartsWith("http")) {
        return Stream(fileName);
      }

      bool wav = fileName.EndsWith(".wav");

      speaking = true;
      WSRConfig.GetInstance().logInfo("PLAYER", "Start MP3 Player");
      using (var ms = File.OpenRead(fileName))
      using (var reader = wav ? (WaveStream) new WaveFileReader(ms) : (WaveStream) new Mp3FileReader(ms))
      using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(reader))
      using (var baStream = new BlockAlignReductionStream(pcmStream))
      using (var waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback())) {
        waveOut.Init(baStream);
        waveOut.Play();
        played.Add(fileName);
        while (baStream.CurrentTime < baStream.TotalTime && played.Contains(fileName)) {
          Thread.Sleep(100);
        }
        played.Remove(fileName);
        waveOut.Stop();
      }
      WSRConfig.GetInstance().logInfo("PLAYER", "End MP3 Player");
      speaking = false;
      return true;
    }

    public bool Stream(string url) {

      if (url == null) { return false; }

      speaking = true;
      WSRConfig.GetInstance().logInfo("PLAYER", "Stream MP3 Player");
      bool wav = url.EndsWith(".wav");

      using (var ms = new MemoryStream())
      using (var stream = WebRequest.Create(url).GetResponse().GetResponseStream()) {
        byte[] buffer = new byte[32768];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
          ms.Write(buffer, 0, read);
        }
        ms.Position = 0;
        using (var reader = wav ? (WaveStream)new WaveFileReader(ms) : (WaveStream)new Mp3FileReader(ms))
        using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(reader))
        using (var baStream = new BlockAlignReductionStream(pcmStream))
        using (var waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback())) {
          waveOut.Init(baStream);
          waveOut.Play();
          played.Add(url);
          while (baStream.CurrentTime < baStream.TotalTime && played.Contains(url)) {
            Thread.Sleep(100);
          }
          played.Remove(url);
          waveOut.Stop();
        }
      }

      WSRConfig.GetInstance().logInfo("PLAYER", "End MP3 Player");
      speaking = false;
      return true;
    }
  }
}
