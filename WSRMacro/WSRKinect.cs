using System;
using System.Globalization;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Kinect;

#if MICRO
using System.Speech.Recognition;
using System.Speech.AudioFormat;
#endif

#if KINECT
using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;
#endif

namespace net.encausse.sarah { 

  public class WSRKinect : WSRMicro {

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    public override void Init() {

      // Start Sensors
      InitSensors();

      // Start WebSocket Server
      WebSocketManager.GetInstance().StartWebSocketServer();

      base.Init();
    }

    public override void Dispose() {

      // Stop super
      base.Dispose();

      // Stop Thread
      WSRCamera.Shutdown();

      // Stop Sensor
      WSRConfig.GetInstance().logInfo("KINECT", "Stop Sensors");
      if (Sensors != null) {
        foreach (WSRKinectSensor sensor in Sensors) 
          if (sensor != null) sensor.Dispose();
      }
    }

    // ==========================================
    //  KINECT INIT SENSOR
    // ==========================================

    public List<WSRKinectSensor> Sensors { get; set; }
    protected void InitSensors() {

      // Cached sensors
      Sensors = new List<WSRKinectSensor>();

      // Looking for a valid sensor 
      foreach (var potentialSensor in KinectSensor.KinectSensors) {
        if (potentialSensor.Status != KinectStatus.Connected) { continue; }
        WSRKinectSensor sensor = new WSRKinectSensor(potentialSensor);
        Sensors.Add(sensor);
      }

      // Little warning
      if (Sensors.Count <= 0) {
        WSRConfig.GetInstance().logError("KINECT", "No Kinect Sensor");
      }
    }

    public WSRKinectSensor ActiveSensor() {
      return Sensors[0];
    }

    // ==========================================
    //  HANDLE SYSTEM TRAY
    // ==========================================

    
    public override void HandleCtxMenu(ContextMenuStrip menu) {

      int i = 0;
      foreach(WSRKinectSensor sensor in Sensors) {

        var name = "Kinect_"+i++;

        // Start Camera window
        WSRCamera.Start(name, sensor);

        // Build CtxMenu
        var item = new ToolStripMenuItem();
        item.Text = name;
        item.Click += new EventHandler(Kinect_Click);
        item.Image = net.encausse.sarah.Properties.Resources.Kinect;

        // Add CtxMenu
        menu.Items.Add(item);
      }

      // Super
      base.HandleCtxMenu(menu);
    }

    void Kinect_Click(object sender, EventArgs e) {
      ToolStripMenuItem menu = sender as ToolStripMenuItem;
      WSRCamera.Display(menu.Text);
    }

    // ==========================================
    //  HANDLE HTTPSERVER
    // ==========================================

    public override bool HandleCustomRequest(NHttp.HttpRequestEventArgs e, StreamWriter writer) {

      if (base.HandleCustomRequest(e, writer)) {
        return true;
      }

      // Parse Result's Photo
      if (e.Request.Params.Get("picture") != null) {

        String path = ActiveSensor().TakePicture("medias/");
        e.Response.ContentType = "image/jpeg";
        Bitmap bmp = (Bitmap) Bitmap.FromFile(path);
        bmp.Save(e.Response.OutputStream, ImageFormat.Jpeg);

        return true;
      }

      // Return Last Height Active Sensor
      var height = e.Request.Params.Get("height");
      if (height != null) {
        double h = WSRProfileManager.GetInstance().Heigth;
        if (height == "tts") {
          WSRSpeakerManager.GetInstance().Speak(h + " mètres", false);
        }
        else {
          writer.Write("" + h);
        }
        return true;
      }

      // Face recognition
      String facereco = e.Request.Params.Get("face");
      if (facereco != null) {
        facereco = e.Server.HtmlDecode(facereco);
        if ("start".Equals(facereco)) {
          WSRConfig.GetInstance().StandByFace = false;
        }
        else if ("stop".Equals(facereco)) {
          WSRConfig.GetInstance().StandByFace = true;
        }
      }

      // Gesture recognition
      String gesture = e.Request.Params.Get("gesture");
      if (gesture != null) {
        gesture = e.Server.HtmlDecode(gesture);
        if ("start".Equals(gesture)) {
          WSRConfig.GetInstance().StandByGesture = false;
        }
        else if ("stop".Equals(gesture)) {
          WSRConfig.GetInstance().StandByGesture = true;
        }
      }
      return false;
    }

    // ==========================================
    //  HANDLE SPEECH RECOGNITION
    // ==========================================

    public override void InitSpeechEngine() {
      
      base.InitSpeechEngine(false);
      
      try {

        WSRConfig cfg = WSRConfig.GetInstance();
        WSRSpeechManager manager = WSRSpeechManager.GetInstance();
        SpeechAudioFormatInfo format = new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null);

        for( int i = 0 ; i < Sensors.Count ; i++) {
          KinectAudioSource source = Sensors[i].Sensor.AudioSource;
          source.EchoCancellationMode = EchoCancellationMode.CancellationAndSuppression;
          source.NoiseSuppression = true;
          source.BeamAngleMode = BeamAngleMode.Adaptive; //set the beam to adapt to the surrounding
          source.AutomaticGainControlEnabled = false;
          if (WSRConfig.GetInstance().Echo >= 0){
            source.EchoCancellationSpeakerIndex = WSRConfig.GetInstance().Echo;
          }

          String prefix = "KINECT_" + i;
          cfg.logInfo(prefix, "AutomaticGainControlEnabled : "  + source.AutomaticGainControlEnabled);
          cfg.logInfo(prefix, "BeamAngle : "                    + source.BeamAngle);
          cfg.logInfo(prefix, "EchoCancellationMode : "         + source.EchoCancellationMode);
          cfg.logInfo(prefix, "EchoCancellationSpeakerIndex : " + source.EchoCancellationSpeakerIndex);
          cfg.logInfo(prefix, "NoiseSuppression : "             + source.NoiseSuppression);
          cfg.logInfo(prefix, "SoundSourceAngle : "             + source.SoundSourceAngle);
          cfg.logInfo(prefix, "SoundSourceAngleConfidence : "   + source.SoundSourceAngleConfidence);

          var stream = source.Start();
          // streamer = new SpeechStreamer(stream); // FIXME
          manager.AddEngine(prefix, cfg.language, cfg.confidence, stream, format);
        }
      }
      catch (Exception ex) {
        WSRConfig.GetInstance().logError("ENGINE", "Init Kinect Engines: " + ex.Message);
      }
    }

    public override String HandleCustomAttributes(XPathNavigator xnav) {
      String path = base.HandleCustomAttributes(xnav);

      // 3.3 Parse Result's Photo
      path = HandlePicture(xnav, path);

      // 3.4 Parse Result's Height
      path = HandleHeight(xnav, path);

      return path;
    }

    protected String HandlePicture(XPathNavigator xnav, String path) {
      XPathNavigator picture = xnav.SelectSingleNode("/SML/action/@picture");
      if (picture != null) {
        path = ActiveSensor().TakePicture("medias/");
      }
      return path;
    }

     protected String HandleHeight(XPathNavigator xnav, String path) {
      XPathNavigator height = xnav.SelectSingleNode("/SML/action/@height");
      if (height != null) {
        double h = WSRProfileManager.GetInstance().Heigth;
        WSRSpeakerManager.GetInstance().Speak(h + " mètres", false);
      }
      return path;
    }
  }
}