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
using Microsoft.Kinect;

/*
using System.Speech.Recognition;
using System.Speech.AudioFormat;
*/

using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;


namespace net.encausse.sarah {

  public class WSRKinectMacro : WSRMacro {

    new public static WSRMacro GetInstance() {
      if (manager == null) {
        manager = new WSRKinectMacro();
        manager.Init();
      }
      return manager;
    }

    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    public override void Init() {
      base.Init();
      SetupSensor();
    }

    public override void Dispose() {
      
      // Stop Thread
      WSRCamera.Shutdown();

      // Stop Sensor
      logInfo("KINECT", "Stop sensor");
      if (Sensor != null) {  Sensor.Stop(); }

      // Stop super
      base.Dispose();
    }

    // ==========================================
    //  WSRMacro SYSTEM TRAY
    // ==========================================

    public override void HandleCtxMenu(ContextMenuStrip menu) {

      if (WSRConfig.GetInstance().facetrack > 0) {
        var item = new ToolStripMenuItem();
        item.Text = "Kinect";
        item.Click += new EventHandler(Kinect_Click);
        item.Image = net.encausse.sarah.Properties.Resources.Kinect;
        menu.Items.Add(item);
      }

      // Super
      base.HandleCtxMenu(menu);
    }

    void Kinect_Click(object sender, EventArgs e) {
      WSRCamera.Display();
    }

    // ==========================================
    //  KINECT GESTURE
    // ==========================================
    GestureManager gestureMgr = null;
    public void SetupSkeletonFrame(KinectSensor sensor) {

      if (!WSRConfig.GetInstance().IsGestureMode()) {
        return;
      }

      // Build Gesture Manager
      gestureMgr = new GestureManager();

      // Load Gestures from directories
      foreach (string directory in WSRConfig.GetInstance().directories) {
        DirectoryInfo d = new DirectoryInfo(directory);
        gestureMgr.LoadGestures(d);
      }

      // Plugin in Kinect Sensor
      logInfo("KINECT", "Starting Skeleton sensor");
      sensor.SkeletonStream.Enable();
    }

    public void HandleGestureComplete(Gesture gesture) {
      WSRHttpManager.GetInstance().SendRequest(gesture.Url);
    }

    // ==========================================
    //  WSRMacro SPEECH RECOGNITION
    // ==========================================

    protected override String HandleCustomAttributes(XPathNavigator xnav) {
      String path = base.HandleCustomAttributes(xnav);

      // 3.3 Parse Result's Photo
      path = HandlePicture(xnav, path);

      return path;
    }

    protected String HandlePicture(XPathNavigator xnav, String path) {
      XPathNavigator picture = xnav.SelectSingleNode("/SML/action/@picture");
      if (picture != null) {
        path = TakePicture("medias/");
      }

      return path;
    }

    // ==========================================
    //  WSRMacro REQUEST
    // ==========================================

    public override bool HandleCustomRequest(NHttp.HttpRequestEventArgs e) {

      if (base.HandleCustomRequest(e)) {
        return true;
      }

      // Parse Result's Photo
      if (e.Request.Params.Get("picture") != null) {
        String path = TakePicture("medias/");
        using (var writer = new StreamWriter(e.Response.OutputStream)) {
          e.Response.ContentType = "image/jpeg";
          Bitmap bmp = (Bitmap)Bitmap.FromFile(path);
          bmp.Save(e.Response.OutputStream, ImageFormat.Jpeg);
        }
        return true;
      }

      // Face recognition
      String facereco = e.Request.Params.Get("face");
      if (facereco != null) {
        facereco = e.Server.HtmlDecode(facereco);
        if ("start".Equals(facereco)) {
          WSRCamera.Recognize(true);
        }
        else if ("stop".Equals(facereco)) {
          WSRCamera.Recognize(false);
        }
        else if ("train".Equals(facereco)) {
          WSRCamera.Train();
        }
      }

      // Gesture recognition
      String gesture = e.Request.Params.Get("gesture");
      if (gesture != null && gestureMgr != null) {
        gesture = e.Server.HtmlDecode(gesture);
        if ("start".Equals(gesture)) {
          gestureMgr.Recognize(true);
        }
        else if ("stop".Equals(gesture)) {
          gestureMgr.Recognize(false);
        }
      }

      return false;
    } 

    // ==========================================
    //  KINECT COLOR FRAME
    // ==========================================

    private byte[] ColorPixels;
    private int ColorW;
    private int ColorH;

    public void SetupColorFrame(KinectSensor sensor) {

      logInfo("KINECT", "Starting Color sensor");

      // Turn on the color stream to receive color frames
      // sensor.ColorStream.Enable(ColorImageFormat.RgbResolution1280x960Fps12);
      sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);

      ColorW = sensor.ColorStream.FrameWidth;
      ColorH = sensor.ColorStream.FrameHeight;

      // Allocate space to put the pixels we'll receive
      ColorPixels = new byte[sensor.ColorStream.FramePixelDataLength];

      // Init Common ----------
      sensor.ColorFrameReady += new EventHandler<ColorImageFrameReadyEventArgs>(handle_ColorFrameReady);

      // Init QRCode ----------
      QRCodeManager qrmgr = new QRCodeManager();
      if (qrmgr.SetupQRCode()) {
        sensor.ColorFrameReady += qrmgr.SensorColorFrameReady;
      }

      // Init WebSocket ----------
      WebSocketManager wsmgr = new WebSocketManager();
      if (wsmgr.SetupWebSocket()) {
        sensor.ColorFrameReady += wsmgr.SensorColorFrameReady;
      }

      // Init WSRCamera ----------
      if (WSRConfig.GetInstance().facetrack > 0) {
        WSRCamera.Start();
      }
    }

    protected void handle_ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {

      using (ColorImageFrame frame = e.OpenColorImageFrame()) {
        if (frame == null) { return; }

        // Copy the pixel data from the image to a temporary array
        frame.CopyPixelDataTo(ColorPixels);
      }

      // Remove transparency
      for (int i = 3; i < ColorPixels.Length; i += 4) { ColorPixels[i] = 255; }
    }


    // ==========================================
    //  KINECT QRCODE
    // ==========================================

    public bool HandleQRCodeComplete(String match) {

      // Play sound effect for feedback
      WSRSpeaker.GetInstance().PlayMP3("medias/qrcode.mp3");

      if (match.StartsWith("http")) {
        WSRHttpManager.GetInstance().SendRequest(match);
      }
      else {
        WSRSpeaker.GetInstance().Speak(match);
      }
      return true;
    }

    // ==========================================
    //  KINECT PICTURE
    // ==========================================

    private WriteableBitmap colorBitmap;
    private String TakePicture(string folder) {

      if (null == Sensor || null == folder) {
        return null;
      }

      if (!WSRConfig.GetInstance().IsPictureMode()) { 
        return null;
      }

      if (null == colorBitmap) {
        colorBitmap = NewColorBitmap();
      }

      Bitmap image = GetColorPNG(colorBitmap);
      BitmapEncoder encoder = new JpegBitmapEncoder();
      String time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);
      String path = folder+"KinectSnapshot-" + time + ".jpg";
      using (FileStream fs = new FileStream(path, FileMode.Create)) {
        image.Save(fs, ImageFormat.Jpeg);
     // image.Save(fs, encoder);
      }
      image.Dispose();

      logInfo("PICTURE", "New picture to: " + path);
      return path;
    }

    // ==========================================
    //  KINECT FACE RECOGNITION
    // ==========================================

    DateTime lastFaceRecognition = DateTime.Now;
    public void HandleFaceComplete(List<String> matches) {

      // Throttle
      var delta = DateTime.Now - lastFaceRecognition;
      if (delta.TotalMilliseconds < 1000 * WSRConfig.GetInstance().facetrack) { return; }
      lastFaceRecognition = DateTime.Now;

      // Send HTTP Request
      var qs = string.Join("&id=", matches.ToArray());
      WSRHttpManager.GetInstance().SendRequest("http://127.0.0.1:8080/sarah/face?id=" + qs);

      return;
    }

    // ==========================================
    //  COLOR BITMAP - UTIL
    // ==========================================

    public WriteableBitmap NewColorBitmap() {
      return new WriteableBitmap(ColorW, ColorH, 96.0, 96.0, PixelFormats.Bgra32, null);
    }

    public void UpdateColorBitmap(WriteableBitmap bitmap) {
      var rect = new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
      bitmap.WritePixels(rect, ColorPixels, bitmap.PixelWidth * PixelFormats.Bgra32.BitsPerPixel / 8, 0);
    }

    public Bitmap GetColorPNG(WriteableBitmap bitmap) {

      // Update ColorBitmap
      UpdateColorBitmap(bitmap);

      BitmapEncoder encoder = new PngBitmapEncoder();

      // Create a png bitmap encoder which knows how to save a .png file
      encoder.Frames.Clear();

      // Create frame from the writable bitmap and add to encoder
      encoder.Frames.Add(BitmapFrame.Create(bitmap));

      Bitmap image = null;
      using (MemoryStream ms = new MemoryStream()) {
        encoder.Save(ms);
        image = (Bitmap)Bitmap.FromStream(ms);
      }

      image.RotateFlip(RotateFlipType.RotateNoneFlipX);
      return image;
    }

    // ==========================================
    //  KINECT AUDIO
    // ==========================================

    public override Boolean SetupDevice(SpeechRecognitionEngine sre) {

      // Abort if there is no sensor available
      if (null == Sensor) {
        logInfo("KINECT", "No Kinect Sensor");
        return false;
      }

      SetupAudioSource(Sensor, sre);

      logInfo("KINECT", "Using Kinect Sensors !"); 
      return true;
    }


    protected Boolean SetupAudioSource(KinectSensor sensor, SpeechRecognitionEngine sre) {
      if (!sensor.IsRunning) {
        logInfo("KINECT", "Sensor is not running"); 
        return false;
      }
      
      // Use Audio Source to Engine
      KinectAudioSource source = sensor.AudioSource;
      source.EchoCancellationMode = EchoCancellationMode.CancellationAndSuppression;
      source.NoiseSuppression = true;
      source.BeamAngleMode = BeamAngleMode.Adaptive; //set the beam to adapt to the surrounding

      WSRConfig.GetInstance().logDebug("KINECT", "AutomaticGainControlEnabled : " + source.AutomaticGainControlEnabled);
      WSRConfig.GetInstance().logDebug("KINECT", "BeamAngle : " + source.BeamAngle);
      WSRConfig.GetInstance().logDebug("KINECT", "EchoCancellationMode : " + source.EchoCancellationMode);
      WSRConfig.GetInstance().logDebug("KINECT", "EchoCancellationSpeakerIndex : " + source.EchoCancellationSpeakerIndex);
      WSRConfig.GetInstance().logDebug("KINECT", "NoiseSuppression : " + source.NoiseSuppression);
      WSRConfig.GetInstance().logDebug("KINECT", "SoundSourceAngle : " + source.SoundSourceAngle);
      WSRConfig.GetInstance().logDebug("KINECT", "SoundSourceAngleConfidence : " + source.SoundSourceAngleConfidence);

      sre.SetInputToAudioStream(source.Start(), new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
      return true;
    }

    protected override String GetDeviceConfidence() {
      if (null == Sensor) { return ""; }
      KinectAudioSource source = Sensor.AudioSource;
      if (source == null) { return ""; }
      return "BeamAngle : " + source.BeamAngle + " "
           + "SourceAngle : " + source.SoundSourceAngle + " "
           + "SourceConfidence : " + source.SoundSourceAngleConfidence;
    }

    // ==========================================
    //  KINECT STATRT/STOP
    // ==========================================

    public KinectSensor Sensor { get; set; }
    protected void SetupSensor() {
      // Looking for a valid sensor 
      foreach (var potentialSensor in KinectSensor.KinectSensors) {
        if (potentialSensor.Status == KinectStatus.Connected) {
          Sensor = potentialSensor;
          break;
        }
      }

      // Abort if there is no sensor available
      if (null == Sensor) {
        logInfo("KINECT", "No Kinect Sensor");
        return;
      }

      // Use Skeleton Engine
      SetupSkeletonFrame(Sensor);

      // Use Color Engine
      SetupColorFrame(Sensor);

      // Starting the sensor                   
      try { Sensor.Start(); }
      catch (IOException) { Sensor = null; return; } // Some other application is streaming from the same Kinect sensor
    }

    /*
    public override void StopRecognizer() { 
      base.StopRecognizer();
      logInfo("KINECT", "Stop sensor"); 
      if (Sensor != null) {
        Sensor.Stop();
      }
      logInfo("KINECT", "Stop sensor...done");
    }
    
    public override void StartRecognizer() {
      logInfo("KINECT", "Start sensor"); 
      if (sensor != null) {
        sensor.Start();
        SetupAudioSource(sensor, GetEngine()); 
      }
      logInfo("KINECT", "Start sensor...done");
      base.StartRecognizer();
    }
    */
  }
}