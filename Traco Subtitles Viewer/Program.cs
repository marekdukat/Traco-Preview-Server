using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ThreadState = System.Threading.ThreadState;

namespace Traco_Subtitles_Viewer
{
    class Program
    {
        static void Main(string[] args)
        {
            var program = new Program();

            var listener = new Thread(program.Listener);
            listener.Start();

            Console.WriteLine("Enter command" + Environment.NewLine + "For help press \"h\"");
            var command = Console.ReadLine();
            while (command != "quit")
            {
                switch (command)
                {
                    case "h":
                    {
                        Console.WriteLine("h - help");
                        Console.WriteLine("c - clean console");
                        Console.WriteLine("quit - exit program");
                        command = Console.ReadLine();
                        break;
                    }
                    case "c":
                    {
                        Console.Clear();
                        Console.WriteLine("Enter command" + Environment.NewLine + "For help press \"h\"");
                        command = Console.ReadLine();
                        break;
                    }
                    default:
                    {
                        Console.WriteLine("unknow command");
                        command = Console.ReadLine();
                        break;
                    }
                }
            }

            if (listener.ThreadState != ThreadState.Aborted)
            {
                listener.Abort();
            }
        }

        private void Listener()
        {
            try
            {
                var listener = new HttpListener();
                var endpoints = new[] {
                    "http://" + ConfigurationManager.AppSettings["ip"] + ":" + ConfigurationManager.AppSettings["port"] + "/subtitle/"
                };

                foreach (var prefix in endpoints)
                {
                    listener.Prefixes.Add(prefix);
                }
                listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                listener.Start();

                while (listener.IsListening)
                {
                    var context = listener.GetContext();
                    try
                    {
                        switch (context.Request.HttpMethod)
                        {
                            case "POST":
                            {
                                var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                                Console.WriteLine(DateTime.Now.ToLocalTime() + " - received message: " + body);

                                var entryJson = JObject.Parse(body);
                                var subtitleJob = entryJson.ToObject<Job>();

                                /*remove previously ass subtitle if exist*/
                                RemoveFiles();
                                
                                /*generate ass file*/
                                var scriptInfo = "[Script Info]\r\nScriptType: v4.00+\r\nPlayResX: 384\r\nPlayResY: 288";
                                var styles = "[V4+ Styles]\r\n" + "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n" + "Style: Default," + subtitleJob.Fontname + "," + subtitleJob.Fontsize + "," + subtitleJob.PrimaryColour + "," + "&Hffffff,&H0,&H0," + subtitleJob.Bold + "," + subtitleJob.Italic + "," + subtitleJob.Underline + "," + subtitleJob.StrikeOut + ",100,100,0," + subtitleJob.Angle + ",1,1,0," + subtitleJob.Alignment + "," + subtitleJob.MarginL + "," + subtitleJob.MarginR + "," + subtitleJob.MarginV + ",0";
                                var events = "[Events]\r\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\nDialogue: 0,0:00:00.00,0:00:00.50,Default,,0,0,0,," + subtitleJob.Text;

                                var fileContent = scriptInfo + "\r\n\r\n" + styles + "\r\n\r\n" + events;
                                File.WriteAllText(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass", fileContent);

                                /*generate png*/
                                var outputPng = ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png";
                                var ffmpegCommand = " -y -f lavfi -i \"color = color = white@0.0:size = " + subtitleJob.Width + "x" + subtitleJob.Height + ",format = rgba,subtitles = subtitle.ass:alpha = 1\" -t 0.04 \"" + outputPng + "\"";
                                GenerateSubtitlePreview(ffmpegCommand, ConfigurationManager.AppSettings["workingDirectory"]);

                                /*answer for mcux*/
                                var notSent = true;
                                var count = 1;
                                while (notSent && count < 6)
                                {
                                    try
                                    {
                                        var pngFile = new FileInfo(outputPng);
                                        var numBytes = pngFile.Length;
                                        var fStream = new FileStream(outputPng, FileMode.Open, FileAccess.Read);
                                        var br = new BinaryReader(fStream);
                                        var bOutput = br.ReadBytes((int) numBytes);
                                        br.Close();
                                        fStream.Close();

                                        context.Response.ContentType = "image/png";
                                        context.Response.ContentLength64 = bOutput.Length;
                                        var outputStream = context.Response.OutputStream;
                                        outputStream.Write(bOutput, 0, bOutput.Length);
                                        outputStream.Close();

                                        notSent = false;
                                    }
                                    catch (Exception)
                                    {
                                        Console.WriteLine("neuspesny pokus " + count);
                                        count++;
                                        Thread.Sleep(int.Parse(ConfigurationManager.AppSettings["pause"]));
                                    }
                                }

                                /*send exception after 5 unsuccessful attempts*/
                                if (count == 6 && notSent)
                                {
                                    var answer = Encoding.UTF8.GetBytes("generated exception");
                                    context.Response.StatusCode = 501;
                                    context.Response.KeepAlive = false;
                                    context.Response.ContentLength64 = answer.Length;

                                    var output = context.Response.OutputStream;
                                    output.Write(answer, 0, answer.Length);
                                    context.Response.Close();
                                }

                                /*remove ass file and png file*/
                                CleanFiles();

                                break;
                            }
                            case "GET":
                            {
                                Console.WriteLine(DateTime.Now.ToLocalTime() + " - received get: " + context.Request.RawUrl);

                                var fontName = context.Request.QueryString["fontname"];
                                var fontSize = context.Request.QueryString["fontsize"];
                                var primaryColour = context.Request.QueryString["primarycolour"];
                                var bold = context.Request.QueryString["bold"];
                                var italic = context.Request.QueryString["italic"];
                                var underline = context.Request.QueryString["underline"];
                                var strikeOut = context.Request.QueryString["strikeout"];
                                var angle = context.Request.QueryString["angle"];
                                var alignment = context.Request.QueryString["alignment"];
                                var marginL = context.Request.QueryString["marginl"];
                                var marginR = context.Request.QueryString["marginr"];
                                var marginV = context.Request.QueryString["marginv"];
                                var text = context.Request.QueryString["text"];
                                var width = context.Request.QueryString["width"];
                                var height = context.Request.QueryString["height"];

                                /*remove previously ass subtitle if exist*/
                                RemoveFiles();

                                /*generate ass file*/
                                var scriptInfo = "[Script Info]\r\nScriptType: v4.00+\r\nPlayResX: 384\r\nPlayResY: 288";
                                var styles = "[V4+ Styles]\r\n" + "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n" + "Style: Default," + fontName + "," + fontSize + "," + primaryColour + "," + "&Hffffff,&H0,&H0," + bold + "," + italic + "," + underline + "," + strikeOut + ",100,100,0," + angle + ",3,1,0," + alignment + "," + marginL + "," + marginR + "," + marginV + ",0";
                                var events = "[Events]\r\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\nDialogue: 0,0:00:00.00,0:00:00.50,Default,,0,0,0,," + text;

                                var fileContent = scriptInfo + "\r\n\r\n" + styles + "\r\n\r\n" + events;
                                File.WriteAllText(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass", fileContent);

                                /*generate png*/
                                var outputPng = ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png";
                                var ffmpegCommand = " -y -f lavfi -i \"color = color = white@0.0:size = " + width + "x" + height + ",format = rgba,subtitles = subtitle.ass:alpha = 1\" -t 0.04 \"" + outputPng + "\"";
                                GenerateSubtitlePreview(ffmpegCommand, ConfigurationManager.AppSettings["workingDirectory"]);

                                /*answer for mcux*/
                                var notSent = true;
                                var count = 1;
                                while (notSent && count < 6)
                                {
                                    try
                                    {
                                        var pngFile = new FileInfo(outputPng);
                                        var numBytes = pngFile.Length;
                                        var fStream = new FileStream(outputPng, FileMode.Open, FileAccess.Read);
                                        var br = new BinaryReader(fStream);
                                        var bOutput = br.ReadBytes((int) numBytes);
                                        br.Close();
                                        fStream.Close();

                                        context.Response.ContentType = "image/png";
                                        context.Response.ContentLength64 = bOutput.Length;
                                        var outputStream = context.Response.OutputStream;
                                        outputStream.Write(bOutput, 0, bOutput.Length);
                                        outputStream.Close();

                                        notSent = false;
                                    }
                                    catch (Exception)
                                    {
                                        Console.WriteLine("neuspesny pokus " + count);
                                        count++;
                                        Thread.Sleep(int.Parse(ConfigurationManager.AppSettings["pause"]));
                                    }
                                }

                                /*send exception after 5 unsuccessful attempts*/
                                if (count == 6 && notSent)
                                {
                                    var answer = Encoding.UTF8.GetBytes("generated exception");
                                    context.Response.StatusCode = 501;
                                    context.Response.KeepAlive = false;
                                    context.Response.ContentLength64 = answer.Length;

                                    var output = context.Response.OutputStream;
                                    output.Write(answer, 0, answer.Length);
                                    context.Response.Close();
                                }

                                /*remove ass file and png file*/
                                CleanFiles();

                                break;
                            }
                            default:
                            {
                                /*server error answer for mcux*/
                                Console.WriteLine(DateTime.Now.ToLocalTime() + " - The server either does not recognize the request method");
                                var answer = Encoding.UTF8.GetBytes("The server either does not recognize the request method, or it lacks the ability to fulfil the request.");
                                context.Response.StatusCode = 501;
                                context.Response.KeepAlive = false;
                                context.Response.ContentLength64 = answer.Length;

                                var output = context.Response.OutputStream;
                                output.Write(answer, 0, answer.Length);
                                context.Response.Close();
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        switch (e.Message)
                        {
                            default:
                            {
                                Console.WriteLine(DateTime.Now.ToLocalTime() + " - generated exception: " + e);
                                break;
                            }
                        }

                        var answer = Encoding.UTF8.GetBytes("generated exception");
                        context.Response.StatusCode = 501;
                        context.Response.KeepAlive = false;
                        context.Response.ContentLength64 = answer.Length;

                        var output = context.Response.OutputStream;
                        output.Write(answer, 0, answer.Length);
                        context.Response.Close();
                    }
                }
                listener.Close();

            }
            catch (Exception exception)
            {
                Console.WriteLine(DateTime.Now.ToLocalTime() + " - generated exception: " + exception);
            }
        }

        private void GenerateSubtitlePreview(string argument, string workingDirectory)
        {
            try
            {
                var p = new Process
                {
                    StartInfo =
                    {
                        FileName = ConfigurationManager.AppSettings["ffmpegPath"],
                        Arguments = argument,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = workingDirectory
                    }
                };
                //p.OutputDataReceived += (sender, args) => HandleTestData(args, jobId, modul);
                //p.ErrorDataReceived += (sender, args) => HandleTestData(args, jobId, modul);
                p.StartInfo.CreateNoWindow = false;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
            }
            catch (Exception exception)
            {
                //_logger.ErrorLog(modul, jobId, "vynimka: " + e);
                Console.WriteLine(DateTime.Now.ToLocalTime() + " - generated exception in function GenerateSubtitlePreview(): " + exception);
            }
        }

        private bool DeleteFile(string fullFileName)
        {
            try
            {
                if (File.Exists(fullFileName))
                {
                    File.Delete(fullFileName);
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Console.WriteLine(DateTime.Now.ToLocalTime() + " - generated exception in function DeleteFile()" + Environment.NewLine + exception);
                return false;
            }
        }

        private void RemoveFiles()
        {
            try
            {
                if (File.Exists(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass"))
                {
                    File.Delete(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass");
                    Console.WriteLine(DateTime.Now.ToLocalTime() + " - File " + ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass has been removed");
                }

                if (File.Exists(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png"))
                {
                    File.Delete(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png");
                    Console.WriteLine(DateTime.Now.ToLocalTime() + " - File " + ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png has been removed");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(DateTime.Now.ToLocalTime() + "generated exception in function CleanOldFiles()" + Environment.NewLine + exception);
            }
        }

        private void CleanFiles()
        {
            try
            {
                if (File.Exists(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass"))
                {
                    if (DeleteFile(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass"))
                    {
                        Console.WriteLine(DateTime.Now.ToLocalTime() + " - File " + ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass has been removed");
                    }
                    else
                    {
                        Console.WriteLine(DateTime.Now.ToLocalTime() + " - File " + ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.ass has not been removed");
                    }
                }

                if (File.Exists(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png"))
                {
                    if (DeleteFile(ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png"))
                    {
                        Console.WriteLine(DateTime.Now.ToLocalTime() + " - File " + ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png has been removed");
                    }
                    else
                    {
                        Console.WriteLine(DateTime.Now.ToLocalTime() + " - File " + ConfigurationManager.AppSettings["workingDirectory"] + "subtitle.png has not been removed");
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(DateTime.Now.ToLocalTime() + " - generated exception in function CleanFiles()" + Environment.NewLine + exception);
            }
        }
    }
}
