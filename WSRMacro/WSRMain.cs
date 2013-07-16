using System;

using System.Windows.Forms;
using System.Collections.Generic;

namespace net.encausse.sarah {

  class WSRMain {
    
    static void Main(string[] args) {

      var config = WSRConfig.GetInstance();
      if (!config.Parse(args).Validate()) {
        return;
      }

      try {
        // Setup logging
        config.SetupLogging();

        // Start Process
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Buid WSRMicro or Kinect
        config.SetupMicro();
        config.GetWSRMicro().Init();
        // Application.Idle += new EventHandler(Application_Idle);

        using (WSRTrayMenu tray = new WSRTrayMenu()) {
          tray.Display();
          Application.Run(); // Make sure the application runs!
        }
      } 
      catch(Exception ex){
        config.logError("CATCH_ALL", ex);
      }
    }
    /*
    static void Application_Idle(object sender, EventArgs args) {
      Application.Idle -= new EventHandler(Application_Idle);
      WSRConfig.GetInstance().GetWSRMicro().Init();
    }*/
  }
}