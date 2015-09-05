﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Dialogue_Data_Entry;
using System.IO;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

namespace Dialogue_Data_Entry
{
    public partial class Form2 : Form
    {
        private FeatureGraph featGraph;
        private QueryHandler myHandler;
        private float featureWeight;
        private float tagKeyWeight;
        private SynchronousSocketListener myServer = null;
        private Thread serverThread = null;
        private volatile bool _shouldStop = false;
        private List<TemporalConstraint> temporalConstraintList;

        public Form2(FeatureGraph myGraph, List<TemporalConstraint> myTemporalConstraintList)
        {
            InitializeComponent();
            //pre-process shortest distance
            myGraph.getMaxDistance();           
            this.featGraph = myGraph;
            this.temporalConstraintList = myTemporalConstraintList;
            //clear discussedAmount
            for (int x = 0; x < featGraph.Features.Count(); x++)
            {
                featGraph.Features[x].DiscussedAmount = 0;
            }
            featureWeight = .6f;
            tagKeyWeight = .2f;
            chatBox.AppendText("Hello, and Welcome to the Query. \r\n");
            inputBox.KeyDown += new KeyEventHandler(this.inputBox_KeyDown);
            this.FormClosing += Window_Closing;
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void inputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                query_Click(sender, e);
            }
        }

        private void query_Click(object sender, EventArgs e)
        {
            string query = inputBox.Text;
            if (myHandler == null)
                myHandler = new QueryHandler(featGraph, temporalConstraintList);

            if (EnglishRadioButton.Checked)
            {
                myHandler.language_mode_display = Constant.EnglishMode;
                if (checkBox1.Checked) { myHandler.language_mode_tts = Constant.EnglishMode; }
                else { myHandler.language_mode_tts = Constant.ChineseMode; }
            }
            else
            {
                myHandler.language_mode_display = Constant.ChineseMode;
                if(checkBox1.Checked) { myHandler.language_mode_tts = Constant.ChineseMode; }
                else { myHandler.language_mode_tts = Constant.EnglishMode; }
            }

            chatBox.AppendText("User: "+query+"\r\n");
            string answer = myHandler.ParseInput(query,false);
            string display = myHandler.ParseOutput(answer, myHandler.language_mode_display);

            chatBox.AppendText("System:" + display + "\r\n");

            if (checkBox2.Checked)
            {
                string text;
                if (checkBox1.Checked) { text = display; }
                else { text = myHandler.ParseOutput(answer, myHandler.language_mode_tts); }
                //MessageBox.Show(text);
                XunfeiFunction.ProcessVoice(text, "audio/out.wav", myHandler.language_mode_tts);
                //MessageBox.Show("success");
                Play_TTS_file("audio/out.wav");
            }
            
            inputBox.Clear(); 
        }

        private void ServerModeButton_Click(object sender, EventArgs e)
        {
            //Start new thread for server
            this.serverThread = new Thread(this.DoWork);
            this.serverThread.Start();
        }

        public void DoWork()
        {
            myServer = new SynchronousSocketListener();
            
            this.Invoke((MethodInvoker)delegate {
                chatBox.AppendText("Waiting for client to connect...");
            });

            myServer.StartListening();
            //myServer.SendDataToClient("Connected");
            
            this.Invoke((MethodInvoker)delegate
            {
                chatBox.AppendText("\nConnected!");
            });
            this._shouldStop = false;
            //Console.WriteLine("Connected.");
            while (!this._shouldStop)
            {
                string query = myServer.ReceieveDataFromClient();
                query = query.Replace("<EOF>", "");
                if (query == "QUIT")
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText("Client: " + query + "\r\n");
                    });
                    break;
                }
                if (query == "Start Recording")
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        StartRecording();
                    });
                    //myServer.SendDataToClient("success");
                    continue;
                }
                if (query.Contains("Stop Recording:"))
                {
                    // parse the string, last substring as the language, assume that the input string is correct
                    string language = query.Split(':')[1];
                    this.Invoke((MethodInvoker)delegate
                    {
                        StopRecording();
                    });

                    string translated_query = null;
                    this.Invoke((MethodInvoker)delegate
                    {
                        translated_query = XunfeiFunction.IatModeTranslate("audio/temp.wav", language);
                    });
                    //MessageBox.Show(translated_query);

                    if (language != "english")
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            translated_query += run_translator(translated_query);
                            Console.WriteLine(translated_query);
                        });
                    }

                    if (translated_query != null)
                    {
                        myServer.SendDataToClient(translated_query);
                    }
                    else
                    {
                        myServer.SendDataToClient("Recording stopped: No speech detected.");
                    }
                    continue;
                }
                /*
                // testing Xunfei TTS
                if (query.Contains("TTS#"))
                {
                    string language = query.Split('#')[1];
                    string preferred_sex = query.Split('#')[2];
                    query = query.Split('#')[3];
                    this.Invoke((MethodInvoker)delegate
                    {
                        XunfeiFunction.ProcessVoice(query, "audio/out.wav", language, preferred_sex);
                    });
                    this.Invoke((MethodInvoker)delegate
                    {
                        Play_TTS_file("audio/out.wav");
                    });
                    myServer.SendDataToClient("TTS completed.");
                    continue;
                }
                */
                if (myHandler == null)
                    myHandler = new QueryHandler(featGraph, temporalConstraintList);
                Console.WriteLine("Query: " + query);
                
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText("Client: " + query + "\r\n");
                });
                
                string answer = myHandler.ParseInput(query, true);
                Console.WriteLine("Answer: " + answer);
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText("System:" + answer + "\r\n");
                });

                if (answer.Contains("##"))
                {
                    string tts = answer.Split(new string[] { "##" }, StringSplitOptions.None)[1];
                    answer = answer.Split(new string[] { "##" }, StringSplitOptions.None)[0];

                    Console.WriteLine("Answer contains ##. Send: " + answer);
                    myServer.SendDataToClient(answer);
                    /*
                    try
                    {
                        File.Delete("audio/out.wav");
                        Console.WriteLine("Deleted audio file");
                    }
                    catch(DirectoryNotFoundException dirnotfound)
                    {
                        Console.WriteLine(dirnotfound.Message);
                    }
                    */
                    Console.WriteLine("tts text: " + tts);
                    
                    this.Invoke((MethodInvoker)delegate
                    {
                        Console.WriteLine("Xunfei begins writing");
                        XunfeiFunction.ProcessVoice(tts, "audio/out.wav", myHandler.language_mode_tts);
                        Console.WriteLine("written new audio file");
                    });

                    Play_TTS_file("audio/out.wav");
            
                                        
                }
                else
                {
                    Console.WriteLine("Send: " + answer);
                    myServer.SendDataToClient(answer);
                }
                Console.WriteLine("The returned answer is " + answer);
            }
            myServer.CloseServer();
            myServer = null;
        }

        public void RequestDoWorkStop()
        {
            this._shouldStop = true;
        }

        private void StopServerbutton_Click(object sender, EventArgs e)
        {
            if (myServer != null)
            {
                //(Doesn't seem to stop the loop)
                this.RequestDoWorkStop();
                myServer.CloseServer();
                this.serverThread.Abort(); //To Do: Not use Abort and terminate by existing function DoWork
                this.serverThread.Join();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopServerbutton_Click(sender, e);
        }

        /*private string ParseOutput(string to_parse, int language_mode)
        {
            string answer = "";
            string[] answers = to_parse.Split(new string[] { "##" }, StringSplitOptions.None);

            for(int i=0; i<answers.Length; i++)
            {
                if(language_mode == Constant.EnglishMode && i%2 == 0)
                {
                    answer += answers[i];
                }
                if(language_mode == Constant.ChineseMode && i%2 == 1)
                {
                    answer += answers[i];
                }
            }
            return answer;
        }*/

        NAudio.Wave.WaveIn sourceStream = null;
        //NAudio.Wave.DirectSoundOut waveOut = null;
        NAudio.Wave.WaveFileWriter waveWriter = null;

        private void sourceStream_DataAvailable(object sender, NAudio.Wave.WaveInEventArgs e)
        {
            if (waveWriter == null) return;

            waveWriter.WriteData(e.Buffer, 0, e.BytesRecorded);
            waveWriter.Flush();
        }

        private void StartRecording()
        {
            // Check if there are sources for input sound
            int numResource = NAudio.Wave.WaveIn.DeviceCount;
            if (numResource == 0) return;

            // Use the first source as default
            sourceStream = new NAudio.Wave.WaveIn();
            // Set wave format
            sourceStream.WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1);

            NAudio.Wave.WaveInProvider waveIn = new NAudio.Wave.WaveInProvider(sourceStream);

            //waveOut = new NAudio.Wave.DirectSoundOut();
            //waveOut.Init(waveIn);

            sourceStream.StartRecording();
            //waveOut.Play(); // plays the audio, serve as demo, can be deleted

            sourceStream.DataAvailable += new EventHandler<NAudio.Wave.WaveInEventArgs>(sourceStream_DataAvailable);
            // Save the file temporarily in the audio folder, note that previous recording will be overwritten
            waveWriter = new NAudio.Wave.WaveFileWriter("audio/temp.wav", sourceStream.WaveFormat);
        }

        private void StopRecording()
        {
            /*
            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }
            */
            if (sourceStream != null)
            {
                sourceStream.StopRecording();
                sourceStream.Dispose();
                sourceStream = null;
            }
            if (waveWriter != null)
            {
                waveWriter.Dispose();
                waveWriter = null;
            }
        }

        private void StartSpeakingbutton_Click(object sender, EventArgs e)
        {
            StartRecording();
        }

        private void StopSpeakingbutton_Click(object sender, EventArgs e)
        {
            StopRecording();
            
            if (EnglishRadioButton.Checked)
            {
                inputBox.Text = XunfeiFunction.IatModeTranslate("audio/temp.wav", "english");
            }
            else if (ChineseRadioButton.Checked)
            {
                inputBox.Text = XunfeiFunction.IatModeTranslate("audio/temp.wav", "chinese");
                inputBox.Text += run_translator(inputBox.Text);
            }
            else { }
            
        }

        private void TTSbutton_Click(object sender, EventArgs e)
        {
            string filename = "audio/out.wav";

            if (EnglishRadioButton.Checked)
            {
                XunfeiFunction.ProcessVoice(inputBox.Text, filename, "english", "male");
            }
            else if (ChineseRadioButton.Checked)
            {
                XunfeiFunction.ProcessVoice(inputBox.Text, filename, "chinese");
            }
            else { }

            Play_TTS_file(filename);
        }
        private void Play_TTS_file(string filename)
        {
            Console.WriteLine("In Play_TTS_file start");
            NAudio.Wave.WaveFileReader audio = new NAudio.Wave.WaveFileReader(filename);
            NAudio.Wave.IWavePlayer player = new NAudio.Wave.WaveOut(NAudio.Wave.WaveCallbackInfo.FunctionCallback());
            player.Init(audio);
            player.Play();
            while (true)
            {
                if (player.PlaybackState == NAudio.Wave.PlaybackState.Stopped)
                {
                    player.Dispose();
                    //MessageBox.Show("disposed");
                    audio.Close();
                    audio.Dispose();
                    break;
                }
            };
            Console.WriteLine("After Play_TTS_File while loop");
        }

        private string run_translator(string text)
        {
            string result = null;
            string version = run_python("detect.py");
            if (version.StartsWith("2."))
            {
                result = run_python("translate.py \"" + text + "\"");
            }
            else
            {
                result = run_python("translate3.py \"" + text + "\"");
            }
            return result;
        }

        private string run_python(string filename)
        {
            //MessageBox.Show(filename);
            string result = null;
            Process p = new Process();
            p.StartInfo.FileName = "python";
            p.StartInfo.WorkingDirectory = "translation/";
            p.StartInfo.Arguments = filename;

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.Start();

            result = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            //MessageBox.Show(result);

            p.Close();
            p.Dispose();
            return result;
        }
    }
}
