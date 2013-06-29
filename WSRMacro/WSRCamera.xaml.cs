using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Microsoft.Kinect;

namespace net.encausse.sarah {
  
  public partial class WSRCamera : Window {

    // ==========================================
    //  STATIC
    // ==========================================
    // http://eprystupa.wordpress.com/2008/07/28/running-wpf-application-with-multiple-ui-threads/
    
    
    private static WSRCamera camera;
    public static void Start() {
      Thread thread = new Thread(() => {
        camera = new WSRCamera();
     // camera.Show();
     // camera.Closed += (sender2, e2) => camera.Dispatcher.InvokeShutdown();
        camera.Closing += (sender2, e2) => { camera.Hide(); e2.Cancel = true; };
        System.Windows.Threading.Dispatcher.Run();
      });
      thread.Name = "Camera";
      thread.SetApartmentState(ApartmentState.STA);
      thread.Start(); 
    }
    
    public static void Display() {
      if (camera == null) { return; }
      camera.Dispatcher.Invoke(new Action(() => camera.Show()));
    }

    public static void Shutdown() {
      if (camera == null) { return; }
      camera.Dispatcher.InvokeShutdown();
    }

    public static void Train() {
      if (camera == null) { return; }
      camera.Dispatcher.Invoke(new Action(() => camera.TrainFace()));
    }

    public static void Recognize(bool start) {
      if (camera == null) { return; }
      camera.Dispatcher.Invoke(new Action(() => camera.RecognizeFace(start)));
    }

    // ==========================================
    //  CONSTRCUTOR
    // ==========================================

    public WSRCamera() {
      WSRConfig.GetInstance().logInfo("CAMERA", "Starting camera");
      
      // Setup controls
      InitializeComponent();

      // Cleanup Thread
      Thread thread = Thread.CurrentThread;
      this.DataContext = new { ThreadId = thread.ManagedThreadId };

      // Init BitMaps
      InitColorBitmap();

      // Init Engine
      WSRFaceRecognition.GetInstance().Setup();
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      // Set the image we display to point to the bitmap where we'll put the image data
      this.Image.Source = this.bitmap;
    }

    // ==========================================
    //  WINDOW
    // ==========================================

    protected bool visible = false;

    new public void Hide() {
      base.Hide();
      visible = false;
      ((WSRKinect)WSRConfig.GetInstance().GetWSRMicro()).Sensor.ColorFrameReady -= this.SensorColorFrameReady;
    }

    new public void Show() {
      base.Show();
      visible = true;
      ((WSRKinect)WSRConfig.GetInstance().GetWSRMicro()).Sensor.ColorFrameReady += this.SensorColorFrameReady;
    }

    private void ButtonScreenshotClick(object sender, RoutedEventArgs e) {
      TrainFace();
    }

    public void TrainFace() {
      if (visible == false) { return; }
      imageTrainedFace.Source = WSRFaceRecognition.GetInstance().TrainFace(textBox_Name.Text);
    }

    public void RecognizeFace(bool start) {
      if (start && !visible) {
        ((WSRKinect)WSRConfig.GetInstance().GetWSRMicro()).Sensor.ColorFrameReady += this.SensorColorFrameReady;
      }

      if (!start && !visible) {
        ((WSRKinect)WSRConfig.GetInstance().GetWSRMicro()).Sensor.ColorFrameReady -= this.SensorColorFrameReady;
      }
    }

    // ==========================================
    //  KINECT IMAGE
    // ==========================================

    private WriteableBitmap bitmap;

    private void InitColorBitmap() {
      WSRKinect wsr = (WSRKinect)WSRConfig.GetInstance().GetWSRMicro();

      // This is the bitmap we'll display on-screen
      this.bitmap = wsr.NewColorBitmap();
      this.bitmap = BitmapFactory.ConvertToPbgra32Format(this.bitmap);
    }

    private void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {

      // Copy pixel array
      ((WSRKinect)WSRConfig.GetInstance().GetWSRMicro()).UpdateColorBitmap(bitmap);
      
      // Update Detection
      WSRFaceRecognition.GetInstance().UpdateDetectionAsync(bitmap); 

      // Draw Detection
      if (visible) {
        DrawDetection();
      }
    }

    // ==========================================
    //  DRAWING RESULTS
    // ==========================================

    private void DrawDetection() {
      this.bitmap.Lock();

      DrawInit();
      DrawBefore(this.bitmap);

      foreach (KeyValuePair<System.Drawing.Rectangle, String> entry in WSRFaceRecognition.GetInstance().GetResults()) {
        var name = entry.Value;
        var r = entry.Key;

        DrawLoop(this.bitmap, r, name);

        if (textBox_Name.Text == "") { 
          textBox_Name.Text = name; 
        }
      }

      DrawAfter(this.bitmap);
      this.bitmap.Unlock();
    }

    WriteableBitmap wBmp1, wBmp2;
    Color color;
    private void DrawInit(){
      if (wBmp1 != null && wBmp2 != null) { return; }
      if (!WSRConfig.GetInstance().terminator) { return; }

      wBmp1 = new WriteableBitmap(new BitmapImage(new Uri(@"Camera/TerminatorHUD1.png", UriKind.Relative)));
      wBmp1 = BitmapFactory.ConvertToPbgra32Format(wBmp1);

      wBmp2 = new WriteableBitmap(new BitmapImage(new Uri(@"Camera/TerminatorHUD2.png", UriKind.Relative)));
      wBmp2 = BitmapFactory.ConvertToPbgra32Format(wBmp2);

      color = Color.FromArgb(255, 255, 255, 255);
    }

    private void DrawBefore(WriteableBitmap bitmap){
      if (!WSRConfig.GetInstance().terminator) { return; }
      bitmap.Blit(new System.Windows.Point(0, 0), wBmp1, new Rect(0, 0, wBmp1.PixelWidth, wBmp1.PixelHeight), Colors.White, WriteableBitmapExtensions.BlendMode.Multiply);
      bitmap.Blit(new System.Windows.Point(0, 0), wBmp2, new Rect(0, 0, wBmp2.PixelWidth, wBmp2.PixelHeight), Colors.White, WriteableBitmapExtensions.BlendMode.Alpha);
    }
    
    private void DrawLoop(WriteableBitmap bitmap, System.Drawing.Rectangle r, String name) {
      if (WSRConfig.GetInstance().terminator) {
        DrawLoop_Terminator(bitmap, r, name);
        return; 
      }
      this.bitmap.DrawRectangle(r.Left, r.Top, r.Width + r.Left, r.Height + r.Top, color);
      this.bitmap.DrawRectangle(r.Left + 1, r.Top + 1, r.Width - 1 + r.Left, r.Height - 1 + r.Top, color);
      DrawText(this.bitmap, name, 16, r.Left, r.Top + r.Height);
    }
    
    private void DrawLoop_Terminator(WriteableBitmap bitmap, System.Drawing.Rectangle r, String name) {
      bitmap.DrawEllipse(r.Left, r.Top, r.Width + r.Left, r.Height + r.Top, color);
      bitmap.DrawEllipse(r.Left + 1, r.Top + 1, r.Width - 1 + r.Left, r.Height - 1 + r.Top, color);
      bitmap.DrawEllipse(r.Left + 2, r.Top + 2, r.Width - 2 + r.Left, r.Height - 2 + r.Top, color);

      var x1 = r.Left + r.Width / 2;
      var y1 = r.Top;

      bitmap.DrawLine(x1, y1, x1 + 50, y1 - 50, color);
      bitmap.DrawLine(x1, y1 - 1, x1 + 50, y1 - 1 - 50, color);
      bitmap.DrawLine(x1, y1 - 2, x1 + 50, y1 - 2 - 50, color);

      bitmap.DrawLine(x1 + 50, y1 - 50, x1 + 100, y1 - 50, color);
      bitmap.DrawLine(x1 + 50, y1 - 50 - 1, x1 + 100, y1 - 50 - 1, color);
      bitmap.DrawLine(x1 + 50, y1 - 50 - 2, x1 + 100, y1 - 50 - 2, color);

      DrawText(bitmap, name, 16, x1 + 100 + 5, y1 - 50 - 20);
    }

    private void DrawAfter(WriteableBitmap bitmap) {

    }

    // ==========================================
    //  DRAW - HELPER
    // ==========================================

    private CultureInfo culture = new CultureInfo("fr-fr");
    private FontStretch stretch = new FontStretch();
    private void DrawText(WriteableBitmap wBmp, String text, int size, int x, int y) {

      // Build text
      FormattedText fText = new FormattedText(text, culture, FlowDirection.LeftToRight,
                            new Typeface(this.FontFamily, FontStyles.Normal, FontWeights.Bold, stretch),
                            size, Brushes.White);

      // Draw text
      DrawingVisual drawingVisual = new DrawingVisual();
      DrawingContext drawingContext = drawingVisual.RenderOpen();
      drawingContext.DrawText(fText, new System.Windows.Point(2, 2));
      drawingContext.Close();

      // Build BMP
      RenderTargetBitmap tBmp = new RenderTargetBitmap(180, 180, 120, 96, PixelFormats.Pbgra32);
      tBmp.Render(drawingVisual);
      wBmp.Blit(new Point(x, y), new WriteableBitmap(tBmp), new Rect(0, 0, tBmp.PixelWidth, tBmp.PixelHeight), Colors.White, WriteableBitmapExtensions.BlendMode.Alpha);
    }
  }
}
