// Marathon Buddy launcher.
// All game files are embedded in this .exe. On start it serves them from a tiny local web server
// (localhost only) and opens the game in its own Microsoft Edge app window. Closing the window quits.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    // fixed port so saved settings (stored per address by the browser) survive between runs
    const int PreferredPort = 47815;

    static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html; charset=utf-8" },
        { ".js", "text/javascript; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
        { ".json", "application/json" },
        { ".png", "image/png" },
        { ".svg", "image/svg+xml" }
    };

    static HttpListener listener;

    [STAThread]
    static int Main()
    {
        int port;
        listener = StartServer(out port);
        if (listener == null)
        {
            MessageBox.Show("Marathon Buddy couldn't start its local server.", "Marathon Buddy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        var serveThread = new Thread(ServeLoop) { IsBackground = true };
        serveThread.Start();

        string url = "http://localhost:" + port + "/";
        string edge = FindEdge();
        if (edge != null)
        {
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarathonBuddy", "WindowProfile");
            Directory.CreateDirectory(profile);
            var psi = new ProcessStartInfo(edge,
                "--app=" + url + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --window-size=1400,860 --autoplay-policy=no-user-gesture-required")
            { UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(); // the game window was closed
            }
        }
        else
        {
            // no Edge: use the default browser and keep serving until the user says stop
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            MessageBox.Show("Marathon Buddy is running in your browser.\n\nClick OK to quit.", "Marathon Buddy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        try { listener.Stop(); } catch { }
        return 0;
    }

    static HttpListener StartServer(out int port)
    {
        for (int p = PreferredPort; p < PreferredPort + 20; p++)
        {
            var l = new HttpListener();
            l.Prefixes.Add("http://localhost:" + p + "/");
            try { l.Start(); port = p; return l; }
            catch { try { l.Close(); } catch { } }
        }
        port = 0;
        return null;
    }

    static void ServeLoop()
    {
        var asm = Assembly.GetExecutingAssembly();
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); } catch { return; }
            try
            {
                string path = Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath).TrimStart('/');
                if (path.Length == 0) path = "index.html";
                if (path.Contains("..")) { ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                using (var s = asm.GetManifestResourceStream("game/" + path))
                {
                    if (s == null) { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }
                    string type;
                    ctx.Response.ContentType = Mime.TryGetValue(Path.GetExtension(path), out type) ? type : "application/octet-stream";
                    ctx.Response.AddHeader("Cache-Control", "no-cache");
                    ctx.Response.ContentLength64 = s.Length;
                    s.CopyTo(ctx.Response.OutputStream);
                }
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    static string FindEdge()
    {
        string[] candidates =
        {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Microsoft\Edge\Application\msedge.exe")
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        return null;
    }
}
