
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace net.encausse.sarah {

  // ==========================================
  //  MANAGER
  // ==========================================
  
  public class WSRProfileManager {

    private static WSRProfileManager manager;
    public static WSRProfileManager GetInstance() {
      if (manager == null) {
        manager = new WSRProfileManager();
      }
      return manager;
    }

    // ------------------------------------------
    //  CONSTRUCTOR
    // ------------------------------------------

    private WSRProfileManager() {
      var path = @"profile\profile.json";
      if (!File.Exists(path)) return;

      var json = File.ReadAllText(path, Encoding.UTF8);
      Profiles = JsonConvert.DeserializeObject<List<WSRProfile>>(json);
      WSRConfig.GetInstance().logInfo("PROFILE", json);
      WSRHttpManager.GetInstance().SendPost("http://127.0.0.1:8080/profiles", "profiles", json);
    }

    public double Heigth {
      get { return Current != null ? Current.Height : 0; }
      private set { }
    }
    
    public WSRProfile Current = null; 

    // ------------------------------------------
    //  PROFIL
    // ------------------------------------------

    public List<WSRProfile> Profiles = new List<WSRProfile>();
    private bool ProfileTimout(WSRProfile profile){
      if (profile.Name != null) return false;
      var timeout = (DateTime.Now - profile.Timestamp) > WSRConfig.GetInstance().FaceTH;
      if (timeout && Current == profile) {
        Current = null;
      }
      return timeout;
    }

    private void ProfileChange(bool change) {
      if (!change) { return; }

      string json = JsonConvert.SerializeObject(Profiles);
      WSRConfig.GetInstance().logInfo("PROFILE", json);

      // Write state to a file
      File.WriteAllText(@"profile\profile.json", json);
    }

    public string CurrentName() {
      if (null == Current) { return ""; }
      var name = Current.Name;
      if (name.StartsWith("Unknow")) { return ""; }
      return Current.Name;
    }

    // ------------------------------------------
    //  SETTER
    // ------------------------------------------
    
    protected bool UpdateFace(string name) {
      if (null == name) { return false; }
      bool hasChange = true;

      // Search for WSRProfile
      foreach (WSRProfile profile in Profiles) {
        if (profile.Name != name) { continue; }
        hasChange = false;
        Current = profile; break;
      }

      // Create new WSRProfile
      if (null == Current || (Current.Name != name 
                          && !Current.Name.StartsWith("Unknow"))) { 
        Current = new WSRProfile();
        Profiles.Add(Current);
      }

      // Reset TimeStamp
      Current.Timestamp = DateTime.Now;
      Current.Name = name;
      return hasChange;
    }

    public void UpdateFace(string[] names) {
      bool hasChange = Profiles.RemoveAll(ProfileTimout) > 0;
      foreach (var name in names) {
        hasChange = hasChange || UpdateFace(name);
      }

      // Trigger
      ProfileChange(hasChange);
    }

    public void UpdatePitch(double pitch) {
      bool hasChange = Profiles.RemoveAll(ProfileTimout) > 0;
      
      // Search for WSRProfile
      var delta = null != Current ? Math.Abs(Current.Pitch - pitch) : pitch;
      foreach (WSRProfile profile in Profiles) {
        var d = Math.Abs(profile.Pitch - pitch);
        if (delta < d) { continue; }
        Current = profile;
        delta = d;
      }

      // Create new WSRProfile
      if (null == Current || (Current.Pitch > 0 && delta > WSRConfig.GetInstance().PitchDelta)) {
        Current = new WSRProfile();
        Profiles.Add(Current);
      }

      // Reset TimeStamp
      Current.Timestamp = DateTime.Now;
      Current.Pitch = pitch;

      // Trigger
      ProfileChange(hasChange);
    }

    public void UpdateMood(double mood) {

      // Create new WSRProfile
      if (null == Current) {
        Current = new WSRProfile();
        Profiles.Add(Current);
      }

      // Update Height
      Current.Timestamp = DateTime.Now;
      Current.Mood = mood;
    }

    public void UpdateHeight(double height) {

      // Create new WSRProfile
      if (null == Current) {
        Current = new WSRProfile();
        Profiles.Add(Current);
      }

      // Update Height
      Current.Timestamp = DateTime.Now;
      Current.Height = height;
    }

    public void UpdateHead(float x, float y, float z) {

      // Create new WSRProfile
      if (null == Current) {
        Current = new WSRProfile();
        Profiles.Add(Current);
      }

      // Update Height
      Current.Timestamp = DateTime.Now;
      Current.x = x;
      Current.y = y;
      Current.z = z;
    }
  }

  // ==========================================
  //  PROFILE
  // ==========================================

  public class WSRProfile {

    private static DateTime Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public WSRProfile() { 
      var now = (long)(DateTime.UtcNow - Jan1st1970).TotalMilliseconds % 10000;
      this.Name = "Unknow_" + now;
    }

    public DateTime Timestamp = DateTime.Now;

    // ------------------------------------------
    //  NAME
    // ------------------------------------------

    public string Name = "";

    // ------------------------------------------
    //  VOICE
    // -----------------------------------------

    public double Pitch = 0;

    // ------------------------------------------
    //  HEAD
    // -----------------------------------------

    public float x = 0;
    public float y = 0;
    public float z = 0;

    // ------------------------------------------
    //  MOOD
    // -----------------------------------------

    private List<double> MoodVariance = new List<double>();
    public double Mood {
      get {
        return Math.Round(MoodVariance.Mean(), 2);
      }
      set {
        MoodVariance.Add(value);
        if (MoodVariance.Count > 250) {
          MoodVariance.Clear();
        }
      }
    }

    // ------------------------------------------
    //  HEIGHT
    // -----------------------------------------

    private List<double> HeightVariance = new List<double>();
    public double Height {
      get { 
        return Math.Round(HeightVariance.Mean(), 2); 
      }
      set {
        HeightVariance.Add(value);
        if (HeightVariance.Count > 500 || HeightVariance.StandardDeviation() > 0.1) {
          HeightVariance.Clear();
        }
      }
    }

  }
}
