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

        // Buid WSRMicro or Kinect
        config.SetupMicro();

        // Start Process
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (WSRTrayMenu tray = new WSRTrayMenu()) {
          tray.Display();
          Application.Run(); // Make sure the application runs!
        }
      } 
      catch(Exception ex){
        config.logError("CATCH_ALL", ex);
      }
    }
  }
}