using System;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.IO;
using System.Xml.XPath;
using System.Net;
using System.Threading;
using System.Text;

namespace encausse.net
{
    /**
     * AUTOMATE
     * ********
     *                         * * * * * * * * * * * * *
     *                         *                       *
     * Main => SetGrammar => Start => Recognized => Complete
     *             *           *           *
     *             *           *           *
     *          Watcher   LoadGrammar    HTTP
     */

    class WSRMacro {

        // -----------------------------------------
        //  MAIN
        // -----------------------------------------

        static void Main(string[] args){

            String directory = "macros";
            if (args.Length == 1){
                directory = args[0];
            }

            try {
                WSRMacro wsr = new WSRMacro(directory);
            } 
            catch (Exception ex){
                Console.WriteLine(ex);
            }

            // Keep the console window open.
            Console.ReadLine();
        }

        // -----------------------------------------
        //  WSRMacro VARIABLES
        // -----------------------------------------

        private double CONFIDENCE = 0.82;
        private SpeechRecognitionEngine recognizer = null;

        private String directory = null;
        private String abspath   = null; // Resolved absolute path
        private FileSystemWatcher watcher = null;
        
        private int loading = 0;       // Files to load
        private Boolean load = false;  // Flags to trigger load;
        private Boolean start = false; // Recognizer status

        // -----------------------------------------
        //  WSRMacro CONSTRUCTOR
        // -----------------------------------------

        public WSRMacro() { }

        public WSRMacro(String directory) {
            SetGrammar(directory);
        }

        // -----------------------------------------
        //  WSRMacro METHODS
        // -----------------------------------------

        public Grammar GetGrammar(String file) {
            Grammar grammar = new Grammar(file);
            return grammar;
        }

        public void LoadGrammar(String file, String name){

            // Get the Grammar
            Grammar grammar = GetGrammar(file);
            grammar.Name = name;

            // Get recognizer
            SpeechRecognitionEngine sre = GetEngine();

            // Load the grammar object to the recognizer.
            Console.WriteLine("[Grammar] Load file: " + name + " : " + file);
            sre.LoadGrammarAsync(grammar);
        }

        protected void LoadGrammar() {
            this.loading = 0;

            // Unload All Grammar
            Console.WriteLine("[Grammar] Unload");
            SpeechRecognitionEngine sre = GetEngine();
            sre.UnloadAllGrammars();

            // Load Grammar
            DirectoryInfo dir = new DirectoryInfo(directory);
            abspath = dir.FullName;
            Console.WriteLine("[Grammar] Load directory: " + abspath);
            foreach (FileInfo f in dir.GetFiles("*.xml")) {
                this.loading++;
                LoadGrammar(f.FullName, f.Name);
            }
        }

        public void SetGrammar(String directory) {

            if (!Directory.Exists(directory)) {
                throw new Exception("Macro's directory do not exists: " + directory);
            }

            // Stop Directory Watcher
            StopDirectoryWatcher();

            this.directory = directory;
            this.load = true;

            // Set path to watcher
            GetDirectoryWatcher().Path = directory;

            // Start Automate
            StartRecognizer();
        }

        // ------------------------------------------
        //  WSRMacro WATCHER
        // ------------------------------------------

        public FileSystemWatcher GetDirectoryWatcher() {
            if (watcher != null) {
                return watcher;
            }

            Console.WriteLine("[Watcher] Init watcher");
            watcher = new FileSystemWatcher();
            watcher.Path = directory;
            watcher.Filter = "*.xml";
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Changed += new FileSystemEventHandler(watcher_Changed);

            return watcher;
        }

        public void StartDirectoryWatcher() {
            if (!GetDirectoryWatcher().EnableRaisingEvents) {
                Console.WriteLine("[Watcher] Start watching");
                GetDirectoryWatcher().EnableRaisingEvents = true;
            }
        }

        public void StopDirectoryWatcher() {
            if (GetDirectoryWatcher().EnableRaisingEvents) {
                Console.WriteLine("[Watcher] Stop watching");
                GetDirectoryWatcher().EnableRaisingEvents = false;
            }
        }

        // ------------------------------------------
        //  WSRMacro ENGINE
        // ------------------------------------------

        public SpeechRecognitionEngine GetEngine() {
            if (recognizer != null) {
                return recognizer;
            }
            
            // For Kinect use this:
            // http://social.msdn.microsoft.com/Forums/en-US/kinectsdkaudioapi/thread/f184a652-a63f-4c72-a807-f9770fdf57f8

            Console.WriteLine("[Engine] Init recognizer");
            recognizer = new SpeechRecognitionEngine();

            // Add a handler for the LoadGrammarCompleted event.
            recognizer.LoadGrammarCompleted += new EventHandler<LoadGrammarCompletedEventArgs>(recognizer_LoadGrammarCompleted);

            // Add a handler for the SpeechRecognized event.
            recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);

            // Add a handler for the SpeechRecognizedCompleted event.
            recognizer.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);

            // Add a handler for the AudioStateChangedEvent event.
            recognizer.AudioStateChanged += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);

            // Set recognizer properties
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
            recognizer.BabbleTimeout = TimeSpan.FromSeconds(2);
            recognizer.EndSilenceTimeout = TimeSpan.FromSeconds(1);
            recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

            Console.WriteLine("BabbleTimeout: {0}", recognizer.BabbleTimeout);
            Console.WriteLine("InitialSilenceTimeout: {0}", recognizer.InitialSilenceTimeout);
            Console.WriteLine("EndSilenceTimeout: {0}", recognizer.EndSilenceTimeout);
            Console.WriteLine("EndSilenceTimeoutAmbiguous: {0}", recognizer.EndSilenceTimeoutAmbiguous);

            // Set Max Alternate to 0
            recognizer.MaxAlternates = 0;
            Console.WriteLine("MaxAlternates: {0}", recognizer.MaxAlternates);

            // Set the input to the recognizer.
            recognizer.SetInputToDefaultAudioDevice();

            return recognizer;
        }

        // See also: http://msdn.microsoft.com/en-us/library/ms554584.aspx
        public void StartRecognizer() {
            
            // Request a loading of grammar
            if (this.load){
              this.load = false;
              LoadGrammar();
              return;
            }

            // Prevent lasting call during grammar
            if (this.loading > 0) { return; }

            // Start Directory Watcher
            StartDirectoryWatcher();

            // Start Recognizer
            if (!this.start) {
              this.start = true;
              GetEngine().RecognizeAsync(RecognizeMode.Single);
              // Console.WriteLine("[Engine] Start listening " + GetEngine().AudioLevel);
            }
        }

        public void StopRecognizer() {
            Console.WriteLine("[Engine] Stop listening");
            GetEngine().RecognizeAsyncStop();
        }

        // ------------------------------------------
        //  WSRMacro HTTP
        // ------------------------------------------

        protected String GetURL(RecognitionResult result) {
            // Parse Semantics
            XPathNavigator xnav = result.ConstructSmlFromSemantics().CreateNavigator();
            Console.WriteLine(xnav.OuterXml);

            // Build URI
            String url = xnav.SelectSingleNode("/SML/action/@uri").Value;

            // Build QueryString
            String prefix = "?";
            XPathNodeIterator it = xnav.Select("/SML/action/*");
            while (it.MoveNext()) {
                if (it.Current.Name == "confidence") continue;
                if (it.Current.Name == "uri") continue;
                url += prefix + it.Current.Name + "=" + it.Current.Value;
                prefix = "&";
            }

            // Append Directory Path
            url += prefix + "directory=" + abspath;

            return url;
        }

        protected void SendRequest(String url) {
            if (url == null) { return; }

            HttpWebRequest req = (HttpWebRequest) WebRequest.Create(url);
            req.Method = "GET";

            Console.WriteLine("[HTTP] Send HttpRequest: " + req.Address);
            
            try {
                HttpWebResponse res = (HttpWebResponse)req.GetResponse();
                Console.WriteLine("[HTTP] Response status: {0}", res.StatusCode);

                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)){
                  say(sr.ReadToEnd());
                }
            }
            catch (WebException ex){
                Console.WriteLine("[HTTP] Exception: " + ex.Message);
            }
        }

        // -----------------------------------------
        //  WSRMacro SPEECH
        // -----------------------------------------

        public void say(String tts) {
            Console.WriteLine("[TTS] Say: {0}", tts);
            using (SpeechSynthesizer synthesizer = new SpeechSynthesizer()) {

                // Configure the audio output.
                synthesizer.SetOutputToDefaultAudioDevice();

                // Build and speak a prompt.
                PromptBuilder builder = new PromptBuilder();
                builder.AppendText(tts);
                synthesizer.Speak(builder);
            }
        }

        // -----------------------------------------
        //  WSRMacro LISTENERS
        // -----------------------------------------

        // Handle the LoadGrammarCompleted event.
        protected void recognizer_LoadGrammarCompleted(object sender, LoadGrammarCompletedEventArgs e){
            Console.WriteLine("[Grammar]  Loaded: " + e.Grammar.Name);
            this.loading--;
            StartRecognizer();
        }

        // Handle the SpeechRecognized event.
        protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
            RecognitionResult rr = e.Result;

            if (rr.Confidence < CONFIDENCE) {
                Console.WriteLine("[Engine] Speech rejected: " + rr.Confidence + " Text: " + rr.Text);
                return;
            }

            Console.WriteLine("[Engine] Speech recognized: " + rr.Confidence + " Text: " + rr.Text);
            SendRequest(GetURL(rr));
        }

        // Handle the SpeechRecognized event.
        protected void recognizer_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e) {
            Console.WriteLine("[Engine] RecognizeCompleted ({0}):", DateTime.Now.ToString("mm:ss.f"));
            string resultText = e.Result != null ? e.Result.Text : "<null>";
            // Console.WriteLine("[Engine]  BabbleTimeout: {0}; InitialSilenceTimeout: {1}; Result text: {2}", e.BabbleTimeout, e.InitialSilenceTimeout, resultText);
            this.start = false;
            StartRecognizer();
        }

        // Handle Audio state changed event
        static void recognizer_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
            // Console.WriteLine("[Engine] AudioStateChanged ({0}): {1}", DateTime.Now.ToString("mm:ss.f"), e.AudioState);
        }

        // Handle Grammar change event
        protected void watcher_Changed(object sender, FileSystemEventArgs e) {
            SetGrammar(this.directory);
        }
    }
}