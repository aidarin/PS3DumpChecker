﻿namespace PS3DumpChecker {
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Windows.Forms;
    using PS3DumpChecker.Properties;

    internal sealed partial class UpdateForm : Form {
        internal UpdateForm() {
            InitializeComponent();
            Icon = Program.AppIcon;
        }

        private void UpdateFormLoad(object sender, EventArgs e) {
            statuslbl.Text = Resources.downloadchangelog;
            changelogbw.RunWorkerAsync();
        }

        private static string GetFinalUrl(string url) {
            var location = url;
            do {
                url = location;
                var req = WebRequest.Create(url) as HttpWebRequest;
                if(req == null)
                    break;
                req.Method = "HEAD";
                req.AllowAutoRedirect = false;
                try {
                    using(var response = req.GetResponse() as HttpWebResponse) {
                        if(response != null)
                            location = response.GetResponseHeader("Location");
                    }
                }
                catch(WebException) {
                    return url; //Not getting any further anyways...
                }
            }
            while(location != url && !string.IsNullOrEmpty(location));
            return url;
        }

        private void BWDoWork(object sender, DoWorkEventArgs e) {
            try {
                var url = GetFinalUrl("https://github.com/Swizzy/PS3DumpChecker/raw/master/Latest%20Compiled%20Version/changelog");
                var wc = new WebClient();
                e.Result = wc.DownloadString(url);
                if(string.IsNullOrEmpty(e.Result as string))
                    e.Result = "Oh noes! There is no changelog for you :'(";
            }
            catch(WebException ex) {
                var code = ((HttpWebResponse) ex.Response).StatusCode;
                if(code == HttpStatusCode.NoContent || code == HttpStatusCode.NotFound)
                    e.Result = "Oh noes! There is no changelog for you :'(";
                else
                    e.Result = string.Format("Oh noes! You've found a bug!! Please copy 'n' paste the error below and send to me... {0}{0}{0}{1}", Environment.NewLine, ex);
            }
        }

        private void SetBtns(bool state) {
            appbtn.Enabled = state;
            cfgbtn.Enabled = state;
            hashlistbtn.Enabled = state;
        }

        private void BWRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
            statuslbl.Text = Resources.changelogdone;
            changelog.Text = e.Result as string;
            SetBtns(true);
        }

        private void DwlCompleted() {
            statuslbl.Text = Resources.downloadcompleted;
            SetBtns(true);
        }

        private void DownloadFile(string file) {
            SetBtns(false);
            dwlbw = new BackgroundWorker();
            switch(file) {
                case "latest.cfg":
                    dwlbw.RunWorkerCompleted += CfgDwlCompleted;
                    break;
                case "latest.hashlist":
                    dwlbw.RunWorkerCompleted += HashListDwlCompleted;
                    break;
                case "latest.exe":
                    dwlbw.RunWorkerCompleted += ExeDwlCompleted;
                    break;
                default:
                    dwlbw.RunWorkerCompleted += (sender, args) => DwlCompleted();
                    break;
            }
            dwlbw.DoWork += DwlDoWork;
            dwlbw.RunWorkerAsync(file);
        }

        private void CfgDwlCompleted(object sender, RunWorkerCompletedEventArgs e) {
            if(CheckHash("latest.cfg")) {
                Program.MainForm.ParseConfig("latest.cfg");
                MoveFile("latest.cfg", "default.cfg");
            }
            else
                MessageBox.Show(Resources.baddwl);
            DwlCompleted();
        }

        private void HashListDwlCompleted(object sender, RunWorkerCompletedEventArgs runWorkerCompletedEventArgs) {
            if(CheckHash("latest.hashlist")) {
                Common.Hashes = new HashCheck("latest.hashlist");
                MoveFile("latest.hashlist", "default.hashlist");
            }
            else
                MessageBox.Show(Resources.baddwl);
            DwlCompleted();
        }

        private void ExeDwlCompleted(object sender, RunWorkerCompletedEventArgs runWorkerCompletedEventArgs) {
            if(CheckHash("latest.exe")) {
                var fi = new FileInfo(Path.GetTempPath() + "UpdateHelper.exe");
                Program.ExtractResource(fi, "UpdateHelper.exe", false);
                var dir = Path.GetDirectoryName(Application.ExecutablePath);
                if(string.IsNullOrEmpty(dir))
                    throw new InvalidOperationException();
                var cproc = Process.GetCurrentProcess();
                var proc = new Process {
                    StartInfo = {
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        FileName = fi.FullName,
                        Arguments = string.Format("\"{0}\" \"latest.exe\" \"{1}\"", cproc.Id, cproc.MainModule.FileName)
                    }
                };
                proc.Start();
                return;
            }
            MessageBox.Show(Resources.baddwl);
            DwlCompleted();
        }

        private static void DwlDoWork(object sender, DoWorkEventArgs e) {
            try {
                var file = e.Argument as string;
                if(file == null)
                    throw new ArgumentNullException(Resources.NoFile, new Exception(Resources.NoFile));
                var hfile = file.Equals("latest.exe", StringComparison.CurrentCultureIgnoreCase) ? "PS3DumpChecker.exe" : string.Format("default{0}", file.Substring(file.LastIndexOf('.')));
                var url = GetFinalUrl(string.Format("{0}/{1}?raw=true", "https://github.com/Swizzy/PS3DumpChecker/raw/master/Latest%20Compiled%20Version", hfile));
                var wc = new WebClient();
                wc.DownloadFile(url, file);
            }
            catch(WebException ex) {
                MessageBox.Show(((HttpWebResponse) ex.Response).StatusDescription, Resources.httperror, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void MoveFile(string src, string target) {
            File.Delete(target);
            File.Move(src, target);
        }

        private static bool CheckHash(string file, string hfile = null, string real = null) {
            FileStream tmp = null;
            try {
                if(!File.Exists(file))
                    return false; // It doesn't even exist... why bother?
                var wc = new WebClient();
                if(string.IsNullOrEmpty(hfile))
                    hfile = file.Equals("latest.exe", StringComparison.CurrentCultureIgnoreCase) ? "PS3DumpChecker.exe" : string.Format("default{0}", file.Substring(file.LastIndexOf('.')));
                var url = GetFinalUrl(string.Format("{0}/{1}.md5", "https://github.com/Swizzy/PS3DumpChecker/raw/master/Latest%20Compiled%20Version", hfile));
                var hash = wc.DownloadString(url);
                if(string.IsNullOrEmpty(hash))
                    return false;
                tmp = new FileStream(file, FileMode.Open);
                if(!string.IsNullOrEmpty(real))
                    return real.Equals(hash, StringComparison.CurrentCultureIgnoreCase);
                real = "";
                var thash = MD5.Create().ComputeHash(tmp);
                foreach(var b in thash)
                    real += b.ToString("X2");
                tmp.Close();
                return real.Equals(hash, StringComparison.CurrentCultureIgnoreCase);
            }
            catch(WebException ex) {
                MessageBox.Show(((HttpWebResponse) ex.Response).StatusDescription, Resources.httperror, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally {
                if(tmp != null)
                    tmp.Close();
            }
        }

        public void CfgbtnClick(object sender, EventArgs e) {
            if(CheckHash("default.cfg"))
                return;
            statuslbl.Text = Resources.dlinglatestcfg;
            DownloadFile("latest.cfg");
        }

        public void HashlistbtnClick(object sender, EventArgs e) {
            if(CheckHash("default.hashlist"))
                return;
            statuslbl.Text = Resources.dlinghashlist;
            DownloadFile("latest.hashlist");
        }

        private void AppbtnClick(object sender, EventArgs e) {
            try {
                var md5 = "";
                var tmp = MD5.Create().ComputeHash(File.ReadAllBytes(Assembly.GetAssembly(typeof(UpdateForm)).Location));
                foreach(var b in tmp)
                    md5 += b.ToString("X2");
                if(CheckHash("", "PS3DumpChecker.exe", md5))
                    return;
            }
            catch(Exception) {
            }
            statuslbl.Text = Resources.dlingapp;
            DownloadFile("latest.exe");
        }
    }
}