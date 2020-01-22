/*
Copyright (c) 2014 Stephen Stair (sgstair@akkit.org)
Additional code Miguel Parra (miguelvp@msn.com)

Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.
*/

// 
// the winusbdotnet repo isn't the correct place for this code long term
// Code is here for now for convenience in testing and iteration while being developed.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.IO;

using winusbdotnet.UsbDevices;
using System.Drawing.Drawing2D;

namespace TestSeek
{
    public partial class Form1 : Form
    {
        String localPath = @"c:\seek\";
        SeekThermal thermal;
        Thread thermalThread;
        int frameCount;
        bool stopThread;
        bool m_get_extra_cal;
        bool usignExternalCal;
        bool firstAfterCal;
        bool autoSaveImg;
        bool saveExternalFrames;

        ThermalFrame lastFrame, lastCalibrationFrame, lastReferenceFrame;
        ThermalFrame frameID4, frameID1;
        CalibratedThermalFrame lastUsableFrame, lastRenderedFrame;

        bool[] BadPixelArr = new bool[32448];//32448
        double[] gainCalArr = new double[32448];//32448
        int[] offsetCalArr = new int[32448];//32448
        Bitmap paletteImg;

        Queue<Bitmap> bmpQueue;

        public Form1()
        {
            InitializeComponent();

            DoubleBuffered = true;
            bmpQueue = new Queue<Bitmap>();
            paletteImg = new Bitmap(pictureBox1.Image);

            // Init button trigger to be off.
            m_get_extra_cal = false;
            usignExternalCal = false;
            firstAfterCal = false;
            autoSaveImg = false;
            saveExternalFrames = false;

            var device = SeekThermal.Enumerate().FirstOrDefault();
            if(device == null)
            {
                MessageBox.Show("No Seek Thermal devices found.");
                return;
            }
            thermal = new SeekThermal(device);

            thermalThread = new Thread(ThermalThreadProc);
            thermalThread.IsBackground = true;
            thermalThread.Start();
        }

        void ThermalThreadProc()
        {
            BinaryWriter tw;
            DateTime currentFrameTime = DateTime.Now;
            DateTime previousFrameTime = currentFrameTime;
            DateTime currentTime = DateTime.Now;
            DateTime previousTime = currentTime;
            int framesToCapture = 100;

            // Initialize frame (1 based)
            frameCount = 1;

            // Create the output files to save first 20 frames and associated metadata.
            //bw = new BinaryWriter(new FileStream("data.dat", FileMode.Create));
            tw = new BinaryWriter(new FileStream("data.txt", FileMode.Create));

            while (!stopThread && thermal != null)
            {
                bool progress = false;

                // Get frame
                lastFrame = thermal.GetFrameBlocking();

                // Keep the ID4 and ID1 frame
                switch (lastFrame.StatusByte)
                {
                    case 1://shutter cal
                        frameID1 = lastFrame;
                        firstAfterCal = true;
                        break;
                    case 4://first frame gain cal
                        frameID4 = lastFrame;
                        break;
                    default:
                        break;
                }

                // Time after frame capture
                previousTime = currentTime;
                currentTime = DateTime.Now;

                // Save data and metadata for the first framesToCapture frames
                if (frameCount <= framesToCapture)
                {
                    tw.Write(Encoding.ASCII.GetBytes(String.Format("Frame {0} ID {1}\n", frameCount, lastFrame.RawDataU16[10])));
                    tw.Write(Encoding.ASCII.GetBytes(String.Format(lastFrame.AvgValue.ToString())));
                    tw.Write(Encoding.ASCII.GetBytes(String.Format("\n")));

                    if (frameCount == framesToCapture)
                    {
                        tw.Close();
                    }
                }

                switch (lastFrame.StatusByte)
                {
                    case 4://prvi frame za izračuna gaina
                        markBadPixels();
                        getGainCalibration();
                        //konec: prvi frame za izračuna gaina
                        break;
                    case 1://shutter frame za izračun offseta
                        markBadPixels();
                        applyGainCalibration();
                        if (!usignExternalCal) getOffsetCalibration();
                        lastCalibrationFrame = frameID1;
                        saveExternalFrames = false;
                        //konec: shutter frame
                        break;
                    case 3://pravi slikovni frame
                        markZeroPixels();
                        applyGainCalibration();

                        if (m_get_extra_cal)//if this pixel should be used as reference
                        {
                            m_get_extra_cal = false;
                            usignExternalCal = true;
                            getOffsetCalibration();
                            saveExternalFrames = true;
                        }

                        applyOffsetCalibration();
                        fixBadPixels();
                        lastUsableFrame = lastFrame.ProcessFrameU16(lastReferenceFrame, frameID4);
                        progress = true;
                        //konec: pravi slikovni frame
                        break;
                    default:
                        break;
                }

                // Increase frame count.
                frameCount++;

                if(progress)
                {
                    Invalidate();//ponovno izriši formo...
                }
            }
        }

        private void markBadPixels()
        {
            ushort[] RawDataArr = lastFrame.RawDataU16;

            for(int i=0;i<RawDataArr.Length;i++)
            {
                if ((RawDataArr[i] < 1000) || (RawDataArr[i] > (12000)))
                {
                    BadPixelArr[i] = true;
                }
            }
        }

        private void markZeroPixels()
        {
            ushort[] RawDataArr = lastFrame.RawDataU16;

            for (int i = 0; i < RawDataArr.Length; i++)
            {
                if (RawDataArr[i]==0)
                {
                    BadPixelArr[i] = true;
                }
            }
        }

        private void getGainCalibration()
        {
            //gainCalArr
            ushort[] RawDataArr = lastFrame.RawDataU16;

            for (int i = 0; i < RawDataArr.Length; i++)
            {
                if ((RawDataArr[i] >= 1000) && (RawDataArr[i] <= (12000))){
                    gainCalArr[i] = (double)lastFrame.AvgValue / (double)RawDataArr[i];
                }
                else {
                    gainCalArr[i] = 1;
                }
            }
        }

        private void applyGainCalibration()
        {
            //gainCalArr
            ushort[] RawDataArr = lastFrame.RawDataU16;

            for (int i = 0; i < RawDataArr.Length; i++)
            {
                if (!BadPixelArr[i])
                {
                    lastFrame.RawDataU16[i] = (ushort)(RawDataArr[i] * gainCalArr[i]);
                }
            }
        }

        private void getOffsetCalibration()
        {
            //offsetCalArr
            ushort[] RawDataArr = lastFrame.RawDataU16;

            for (int i = 0; i < RawDataArr.Length; i++)
            {
                if (!BadPixelArr[i])
                {
                    offsetCalArr[i] = lastFrame.AvgValue - RawDataArr[i];
                }
            }
        }

        private void applyOffsetCalibration()
        {
            //offsetCalArr
            ushort[] RawDataArr = lastFrame.RawDataU16;

            for (int i = 0; i < RawDataArr.Length; i++)
            {
                if (!BadPixelArr[i])
                {
                    lastFrame.RawDataU16[i] =(ushort)(RawDataArr[i] + offsetCalArr[i]);
                }
            }
        }


        private void fixBadPixels()
        {
            int i = 0;
            ushort[] RawDataArr = lastFrame.RawDataU16;

            int[,] frame_pixels = new int[208, 156];

            for (int y = 0; y < 156; ++y)
            {
                for (int x = 0; x < 208; ++x, ++i)
                {
                    frame_pixels[x, y] = RawDataArr[i];
                }
            }
            i = 0;
            int avgVal = 0;
            int[] arrColor = new int[4];
            for (int y = 0; y < 156; y++)
            {
                for (int x = 0; x < 208; x++)
                {

                    if (x > 0 && x < 207 && y > 0 && y < 155)
                    {
                        arrColor[0] = frame_pixels[x, y - 1];//top
                        arrColor[1] = frame_pixels[x, y + 1];//bottom
                        arrColor[2] = frame_pixels[x - 1, y];//left
                        arrColor[3] = frame_pixels[x + 1, y];//right

                        //get average value, but exclude neighbour dead pixels from average:
                        avgVal = (arrColor.Sum() - (arrColor.Min() + arrColor.Max())) / 2;

                        if (BadPixelArr[i] || lastFrame.RawDataU16[i]==0)//if its bad pixel or if val == 0
                        {
                            lastFrame.RawDataU16[i] = (ushort)avgVal;
                            frame_pixels[x, y] = avgVal;
                        }

                    }
                    i++;
                }
            }

            i = 0;
            avgVal = 0;

            for (int y = 0; y < 156; y++)
            {
                for (int x = 0; x < 208; x++)
                {

                    if (x > 0 && x < 207 && y > 0 && y < 155)
                    {
                        arrColor[0] = frame_pixels[x, y - 1];//top
                        arrColor[1] = frame_pixels[x, y + 1];//bottom
                        arrColor[2] = frame_pixels[x - 1, y];//left
                        arrColor[3] = frame_pixels[x + 1, y];//right

                        //get average value, but exclude neighbour dead pixels from average:
                        avgVal = (arrColor.Sum() - (arrColor.Min() + arrColor.Max())) / 2;

                        if (Math.Abs(avgVal - frame_pixels[x, y]) > 100 && avgVal != 0)//if its bad pixel or if val dif is to big to near pixels
                        {
                            lastFrame.RawDataU16[i] = (ushort)avgVal;
                        }
                    }
                    i++;
                }
            }

            arrColor = new int[3];
            //fix first line:
            for (int x = 1; x < 207; x++)
            {
                arrColor[0] = frame_pixels[x, 1];//bottom
                arrColor[1] = frame_pixels[x - 1, 0];//left
                arrColor[2] = frame_pixels[x + 1, 0];//right

                avgVal = (arrColor.Sum() - (arrColor.Min() + arrColor.Max()));

                if ((Math.Abs(avgVal - frame_pixels[x, 0]) > 100) && avgVal != 0)//if val diff is to big to near pixels, then fix it
                {
                    lastFrame.RawDataU16[x] = (ushort)avgVal;
                }
            }

            //fix last line:
            for (int x = 1; x < 206; x++)
            {
                arrColor[0] = frame_pixels[x, 154];//top
                arrColor[1] = frame_pixels[x - 1, 155];//left
                arrColor[2] = frame_pixels[x + 1, 155];//right

                avgVal = (arrColor.Sum() - (arrColor.Min() + arrColor.Max()));

                if ((Math.Abs(avgVal - frame_pixels[x, 155]) > 100) && avgVal != 0)//if val diff is to big to near pixels, then fix it
                {
                    lastFrame.RawDataU16[155 * 208 + x] = (ushort)avgVal;//32240
                }
            }

            //fix first column
            for (int y = 1; y < 155; y++)
            {
                arrColor[0] = frame_pixels[0, y - 1];//top
                arrColor[1] = frame_pixels[1, y];//right
                arrColor[2] = frame_pixels[0,y+1];//bottom

                avgVal = (arrColor.Sum() - (arrColor.Min() + arrColor.Max()));

                if ((Math.Abs(avgVal - frame_pixels[0, y]) > 100) && avgVal != 0)//if val diff is to big to near pixels, then fix it
                {
                    lastFrame.RawDataU16[y * 208] = (ushort)avgVal;
                }
            }
        }

        

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            stopThread = true;
            if (thermal != null)
            {
                thermalThread.Join(500);
                thermal.Deinit();
            }
        }

        public struct palette
        {
            public int r, g, b;
            public palette(int ri, int gi, int bi)
            {
                r = ri;
                g = gi;
                b = bi;
            }
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            CalibratedThermalFrame data = lastUsableFrame;
            if (data == null) return;
            int y;
            if(data != lastRenderedFrame)
            {
                lastRenderedFrame = data;
                // Process new frame
                Bitmap bmp = new Bitmap((data.Width-2), data.Height);
                Bitmap bigImage = new Bitmap(412, 312);
                int c = 0;
                int v;
                ushort maxValue, minValue;

                // Display last sensor min/max values.
                label2.Text = "Max: " + lastFrame.MaxValue;
                label3.Text = "Min: " + lastFrame.MinValue;

                // Display last calibration min/max values (internal or external)
                if (lastReferenceFrame != null && lastReferenceFrame.IsUsableFrame)
                {
                    // Update label for calibration button functionality.
                    label1.Text = "Push button to switch to default calibration";
                    button1.Text = "Calibraton: Use Internal";
                    label6.Text = "Max: " + lastReferenceFrame.MaxValue;
                    label7.Text = "Min: " + lastReferenceFrame.MinValue;
                }
                else
                {
                    // Update label for calibration button functionality.
                    label1.Text = "Face camera down on an even heated surface";
                    button1.Text = "Calibraton: Use Frame";
                    label6.Text = "Max: " + lastCalibrationFrame.MaxValue;
                    label7.Text = "Min: " + lastCalibrationFrame.MinValue;
                }

                //button2.Text = "Automatic";
                maxValue = (ushort)trackBar1.Value;
                minValue = (ushort)trackBar2.Value;



                // Display displayed data min/max value.
                label9.Text = "Max: " + maxValue;
                label10.Text = "Min: " + minValue;

                for (y = 0; y < 156; y++)
                {
                    for (int x = 0; x < 206; x++)
                    {
                        v = data.PixelData[y * 208 + x]; // + data.PixelData[y * 208 + 206] / 10; // no need to use column 207 since we already use frame id 4, max/min will be off if uncommented as well.

                        // Scale data to be within [0-255] for LUT mapping.
                        ushort maxmin = maxValue;
                        maxmin -= minValue;
                        // Avoid divide by 0
                        if (maxmin == 0)
                            maxmin = 1;

                        v = (v - minValue) * 999 / maxmin;
                        if (v < 0)
                            v = 0;
                        if (v > 999)
                            v = 999;

                        // Greyscale output (would always be limited to 256 colors)
                        bmp.SetPixel(x, y, paletteImg.GetPixel(v, 0));
                    }
                }

                using (Graphics gr = Graphics.FromImage(bigImage))
                {
                    gr.SmoothingMode = SmoothingMode.HighQuality;
                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gr.DrawImage(bmp, new Rectangle(0, 0, 412, 312));
                }
                
                //bigImage = new Bitmap(bmp, new Size(412, 312));
                // Queue Image for display
                bmpQueue.Enqueue(bigImage);
                if (bmpQueue.Count > 1) bmpQueue.Dequeue();

                if (autoSaveImg)
                {
                    //if folder does not exists create it:
                    DirectoryInfo di = Directory.CreateDirectory(localPath + "export");

                    //usignExternalCal mora bit true, 
                    if (saveExternalFrames && usignExternalCal)
                    {
                        string sDate = DateTime.Now.ToString("yyyyMMddhhmmssfff");
                        bmp.Save(localPath+@"export\img_" + sDate + ".png");
                    }
                    else if (firstAfterCal)
                    {
                        string sDate = DateTime.Now.ToString("yyyyMMddhhmmss");
                        Random rnd = new Random();
                        bmp.Save(localPath+@"export\shutter_" + sDate + "_" + rnd.Next(100).ToString() + ".png");
                        firstAfterCal = false;
                    }
                }
            }

            y = 10;

            foreach(Bitmap b in bmpQueue.Reverse())
            {
                e.Graphics.DrawImage(b, 10, y);
                y += b.Height + 10;
            }
        }

        // Button to capture external reference or switch to internal shutter.
        private void button1_Click(object sender, EventArgs e)
        {
            m_get_extra_cal = true;
        }

        // Button to toggle between automatic ranging or manual.
        private void button2_Click(object sender, EventArgs e)
        {
        }

        // Not needed events.
        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {

        }

        private void trackBar2_Scroll(object sender, EventArgs e)
        {

        }

        private void cbAutoSave_CheckedChanged(object sender, EventArgs e)
        {
            autoSaveImg = !autoSaveImg;
        }
    }
}
