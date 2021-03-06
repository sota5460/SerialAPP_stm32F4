


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.IO.Ports;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using System.Diagnostics;




namespace SerialScreen_ver1
{
    


    public partial class Form1 : Form
    {

        public int b_rate = 0;
        public int rx_setting = 0; // 0: 指定バイトで読み取り 1:改行コードまで読み取り 2: 1バイトだけ読み取り
        public int buf_thresh_setting = 0;


        public int data_num; // = 200;
        public byte[] receive_buf; // = new byte[data_num];
        public short[] short_buf; // = new short[data_num / 2];

        public int STM32_ADC_Resolution = 4096;
        public int STM32_ADC_Threshold = 500;
        public int STM32_ADC_OnePulseNum = 10; //マイコン側でトリガー入ってから何個サンプルを取得するかの変数。
        public string adc_start_code = "C"; //マイコン側で設定しているadcスタート用コード hexで0x43 ->マイコン側で割り込みありadcとタイマーのスタート
        public string adc_stop_code = "D"; //マイコン側で設定しているadcストップ用コード hexで0x44

        //stopwatch
        Stopwatch sw = new System.Diagnostics.Stopwatch();
        TimeSpan span = new TimeSpan(0,0,0);
        

        //
        // ADC Charts variables
        //
        public ChartArea multi_adc_chartarea = new ChartArea("adc_multi_result");
        public int adc_cnt = 1; // to count adc_num
        public int adc_diplay_num = 1;

        public Series adc1 = new Series();

        public void buf_settings(int rx_buf_bytesize)
        {
            data_num = rx_buf_bytesize;
            receive_buf = new byte[rx_buf_bytesize];
            if (rx_buf_bytesize % 2 == 0)
            {
                short_buf = new short[data_num / 2];
            }
        }


        //public void short_buf_init()
        //{
        //    for (int i = 0; i < data_num / 2; i++)
        //    {
        //        short_buf[i] = 0;
        //    }
        //}

        //
        // MCA variables or etc
        //
        public int mca_flag = 0;

        public int mca_resolution;
        public int chart_horizon;
        
        public int shadow_cnt = 0;

        public int[] mca_result0; // max value out of  STM32_ADC_OnePulseNum
        public int[] mca_result1;
        public int[] mca_result2;

        public int[] mca_hist0; // histgram_data for mca
        public int[] mca_hist1;
        public int[] mca_hist2;

        public int[] mca_hist_adjusted;

        public short[]  mca_shortbuf_shadow0;
        public short[]  mca_shortbuf_shadow1;
        public short[]  mca_shortbuf_shadow2;

        public int[] mca_total_buf; //integrated buffer 

        ChartArea mca_chart_area = new ChartArea("mca_result");
        public Series mca_chart = new Series();

        //mcaグラフの縦軸の設定
        public void mca_resolution_setting(int res)
        {
            switch (res)
            {
                case (0): mca_resolution = 4096; break;
                case (1): mca_resolution = 1024; break;
                case (2): mca_resolution = 256;  break;
            }
        }

        public void chart_horizon_setting(int res)
        {
            switch (res)
            {
                case (0):chart_horizon = 4096; break;
                case (1):chart_horizon = 1024; break;
                case (2):chart_horizon = 256;  break;
            }
        }



        public void mca_hist_init(int mca_hist_num)
        {
            mca_hist0 = new int[mca_hist_num];
            mca_hist1 = new int[mca_hist_num];
            mca_hist2 = new int[mca_hist_num];
        }


        //
        // delegate 
        //
        delegate void SetTextCallback(string text);
        delegate void SetByteCallback(byte[] txt);
        delegate void SetChartCallback(int adc_cnt);
        delegate void SetASCIICallback(string text);

        



        public Form1()
        {
            //short_buf_init();
            InitializeComponent();
            init_manual();

            //グラフの初期化しなくても動作するけど、これしとかないとグラフが真っ白になって見た目がよくない。
            chart_init();
            chart_MCA_init();

            
        }

        public void init_manual()
        {
            button_close.Enabled = false;
            button_adcStart.Enabled = false;
            button_adcStop.Enabled = false;
            button_MCA_ON.Enabled = false;
            button_MCA_OFF.Enabled = false;

            label_adc_threshold_dis.Visible = false;

            textBox_pulse_num.Text = STM32_ADC_OnePulseNum.ToString();
            textBox_threshold.Text = STM32_ADC_Threshold.ToString();

            
            //mca横ラベルの初期化　
            label_TotalCountingTime.Text = span.ToString(@"hh\:mm\:ss");
            label_total_count_mca.Text = 0.ToString();
            label_peak_mca.Text = "横軸：" +0.ToString() + "\n縦軸：" + 0.ToString();

        }


 





        private void Chart_Response(int adc_cnt)
        {
            if(chart1.InvokeRequired)
            {
                SetChartCallback d = new SetChartCallback(Chart_Response);
                BeginInvoke(d, adc_cnt);
            }else
            {
                     multi_chart_update(adc1, short_to_int(short_buf, data_num / 2), data_num / 2); 
            }

        }



        private void serialPort1_DataReceived_1(object sender, SerialDataReceivedEventArgs e)
        {
            
            int read_bytes = serialPort1.BytesToRead;
            Console.Write("read bytes is {0} \n", read_bytes);

            if (read_bytes > data_num)
            {

                serialPort1.Read(receive_buf, 0, data_num);

                //デリミタ設定して、デリミタまで読むを繰り返すパターン　⇒　デリミタをマイコン側でデータ末尾に付加する必要がある＋adcデータが意図せずデリミタと同じになる可能性。
                //serialPort1.NewLine = "\n";
                //string rx_buf = serialPort1.ReadLine();

                //receive_buf = System.Text.Encoding.ASCII.GetBytes(rx_buf);


                //if (buf_thresh_setting < 3) {
                //    goto JUMP_LABEL;
                //  };

                // convert BYTE data to SHORT data
                byte[] temp_byte = new byte[2];


                for (int i = 0; i < data_num / 2; i++)
                {

                    temp_byte[0] = receive_buf[2 * i];
                    temp_byte[1] = receive_buf[2 * i + 1];

                    Console.Write("temp byte is " + BitConverter.ToString(temp_byte) + "\n");

                    short_buf[i] = BitConverter.ToInt16(temp_byte, 0);
                }


                // to display converted short on console 
                //for (int m = 0; m < data_num / 2; m++)
                //{
                //    Console.WriteLine(short_buf[m]);
                //}

                //JUMP_LABEL:


                Chart_Response(adc_cnt);
                //Task.Run(() => Chart_Response(adc_cnt));

                //adc_cnt++;
                //if (adc_cnt == adc_diplay_num+1)
                //{
                //    adc_cnt = 1;
                //};



                //mcaをオンにしたときの処理。
                //処理が重いので、3スレッド使用して、mca_total_buf（最終的なヒストグラムのデータ）を生成する。グラフはmca_total_bufを読んでるだけ。
                if (mca_flag == 1)
                {
                    switch (shadow_cnt)
                    {
                        case (0):
                            make_shadow(mca_shortbuf_shadow0, short_buf);
                            Task.Run(() => mca_thread_function(0));
                            break;
                        case (1):
                            make_shadow(mca_shortbuf_shadow1, short_buf);
                            Task.Run(() => mca_thread_function(1));
                            break;
                        case (2):
                            make_shadow(mca_shortbuf_shadow2, short_buf);
                            Task.Run(() => mca_thread_function(2));
                            break;
                    }

                    shadow_cnt++;
                    if (shadow_cnt == 3) { shadow_cnt = 0; }

                }

                serialPort1.DiscardInBuffer();
            }

        }



        //
        //ADC Chart methods
        //


        public void chart_init()
        {
            buf_settings(1000);


            chart1.ChartAreas.Clear();
            chart1.Series.Clear();

            chart1.ChartAreas.Add(multi_adc_chartarea);

            //initialize series property 
            // property for series that you need to determine -> charttype, points
            // series -> each graph 
            // 1. make new series 2.add property 3. add series to chart  
            adc1.ChartType = SeriesChartType.Line;
            adc1.Color = Color.FromArgb(211,96,21) ;

            for (int i = 0; i < data_num/2; i++)
            {
                adc1.Points.AddXY(i + 1, 0);
            }

            chart1.Series.Add(adc1);
        }

        public void chart_points_clear_all()
        {
          adc1.Points.Clear();
        }

        public void chart_just_update(Series adc)
        {
            chart1.Series.Remove(adc);
            chart1.Series.Add(adc);
        }

        public void multi_chart_update(Series adc, int[] result, int len)
        {
            chart1.Series.Remove(adc);
            adc.Points.Clear();

            for (int i = 0; i < len; i++)
            {
                adc.Points.AddXY(i + 1, result[i]);
            }
            chart1.Series.Add(adc);


        }



        public int[] short_to_int(short[] short_array, int len)
        {
            int[] int_array = new int[len];

            for(int i = 0; i < len; i++)
            {
                int_array[i] = (int)short_array[i];
            }

            return int_array;
        }


        /// <summary>
        /// MCA methods
        /// There are methods related to MCA down below.
        /// </summary>

        public void mca_hist_zero(int[] mca_hist)
        {   
            for(int i = 0; i < mca_hist.Length; i++)
            {
                mca_hist[i] = 0;
            }
        }

        public void MCA_Data_Organizer(int[] mca_result,short[] target_short_array,int len, int One_Pulse_Point)
        {
           // int result_num = (len ) / One_Pulse_Point;

           // mca_result_init(result_num);

            for(int i = 0; i < target_short_array.Length/One_Pulse_Point; i++)
            {
               for(int j = 0; j < One_Pulse_Point; j++)
                {
                    if(mca_result[i]<target_short_array[i*One_Pulse_Point + j])
                    {
                        mca_result[i] = target_short_array[i * One_Pulse_Point + j];
                    }
                  
                }
            }
        }

        public void Histgram_Data_Organizer(int[] mca_hist, int[] data_array)
        {

           // mca_hist_init(mca_resolution - 1);


            for(int i = 0; i < data_array.Length; i++)
            {
                mca_hist[data_array[i]]++;
            }
            
        }


        // You can use this in order to get the peak value from mca_array.
        public int MaxValue_array(int[] target_array, int len)
        {
            int max_value = 0;
            for(int i = 0; i < len; i++)
            {
                if(max_value < target_array[i]) { max_value = target_array[i]; };
            }
            return max_value;
        }

        public void chart_MCA_init()
        {
            //adcの分解能
            //mca_resolution_setting(comboBox_adc_res.SelectedIndex);
            mca_resolution = STM32_ADC_Resolution;

            //mcaヒストグラム作成用のデータ一時保存用配列mca_histを初期化
            //mca_hist_init(mca_resolution);
            mca_arrays_init(mca_resolution);

            //グラフの初期化　グラフを一旦全部消す。⇒再度追加（mca_chart_area）
            chart_MCA.ChartAreas.Clear();
            chart_MCA.Series.Clear();
            chart_MCA.ChartAreas.Add(mca_chart_area);


            //initialize series property 
            // property for series that you need to determine -> charttype, points
            // series -> each graph 
            // 1. make new series 2.add property 3. add series to chart  
            
            //setting charttype.
            mca_chart.ChartType = SeriesChartType.Line;
            //setting color
            mca_chart.Color = Color.FromArgb(78, 115, 166);

            //mcaの結果格納用配列に０を入れる。
            mca_hist_zero(mca_total_buf);

            //横軸を修正するとき用。デフォルトだと4096（12bit）になってる。
            adjust_horizon_chart_init(mca_total_buf);

            for (int i = 0; i < mca_hist_adjusted.Length; i++)
            {
                mca_chart.Points.AddXY(i + 1, mca_hist_adjusted[i]);
            }

            chart_MCA.Series.Add(mca_chart);
        }


        //タイマーで定期的に呼ばれる。
        public void mca_chart_update(int[] mca_hist)
        {
            //mca_hist_adjusted = new int[chart_horizon];

            chart_MCA.Series.Remove(mca_chart);
            mca_chart.Points.Clear();

            adjust_chart_update(mca_hist);

            for (int i = 0; i < mca_hist_adjusted.Length; i++)
            {
                mca_chart.Points.AddXY(i + 1, mca_hist_adjusted[i]);
            }
            chart_MCA.Series.Add(mca_chart);

            ///ラベルとかの更新
            ///
            //総カウント数の表示
            label_total_count_mca.Text = mca_hist_adjusted.Sum().ToString();
            //最大カウント数の表示
            int max_vertical_count = mca_hist_adjusted.Max();

            int max_horizontal_adc_resolutioin = calculate_max_index_adjust_hist(max_vertical_count);

            label_peak_mca.Text = "横軸：" + max_horizontal_adc_resolutioin.ToString() + "\n縦軸：" + max_vertical_count.ToString();
        }

        public void adjust_horizon_chart_init(int[] mca_hist)
        {
            int adjust_num;

            //横軸の値を設定する。 基本的には横軸（chart_horizon）はADCの分解能と一致する。
            chart_horizon = STM32_ADC_Resolution;
            //chart_horizon_setting(comboBox_horizon.SelectedIndex);
            mca_hist_adjusted = new int[chart_horizon];

            //補正用の値
            adjust_num = mca_resolution / chart_horizon;

            for(int i = 0; i < chart_horizon; i++)
            {
                for(int j = 0; j < adjust_num; j++)
                {
                    mca_hist_adjusted[i] += mca_hist[i+j];
                }   
            }
        }

        public void adjust_chart_update(int[] mca_hist)
        {
            int adjust_num;
            mca_hist_adjusted = new int[chart_horizon];

            adjust_num = mca_resolution / chart_horizon;

            for (int i = 0; i < chart_horizon; i++)
            {
                for (int j = 0; j < adjust_num; j++)
                {
                    mca_hist_adjusted[i] += mca_hist[i * adjust_num + j];
                }
            }
        }

        public int calculate_max_index_adjust_hist(int max_value)
        {
            int return_index = 0;

            for(int i = 0; i < mca_hist_adjusted.Length; i++)
            {
                if(max_value == mca_hist_adjusted[i])
                {
                    return_index = i;
                   
                }
            }

            return return_index;
        }


        //1つのパルスの最大値を格納する配列mca_result、adcの生データ一時保存用配列mca_shortbuf_shadow、mcaの結果mca_total_buf, ヒストグラム一時保存データmca_histを初期化する。
        //mcaのヒストグラムを作るための配列をまとめて初期化できる。
        public void mca_arrays_init(int mca_hist_num)
        {
            mca_hist0 = new int[mca_hist_num];
            mca_hist1 = new int[mca_hist_num];
            mca_hist2 = new int[mca_hist_num];

            //mca_result_init(short_buf.Length /  STM32_ADC_OnePulseNum);

            mca_result0 = new int[short_buf.Length /  STM32_ADC_OnePulseNum];
            mca_result1 = new int[short_buf.Length /  STM32_ADC_OnePulseNum];
            mca_result2 = new int[short_buf.Length /  STM32_ADC_OnePulseNum];

            mca_shortbuf_shadow0 = new short[short_buf.Length];
            mca_shortbuf_shadow1 = new short[short_buf.Length];
            mca_shortbuf_shadow2 = new short[short_buf.Length];

            mca_total_buf = new int[mca_resolution];
        }


        //シリアル通信受取ったときの割り込みで呼ばれる関数。
        public void make_shadow(short[] shadow_array, short[] target)
        {
            if (shadow_array.Length != target.Length)
            {
                MessageBox.Show("size is not right. shadow array and target array ");
            }

            for (int i = 0; i < shadow_array.Length; i++)
            {
                shadow_array[i] = target[i];
            }
        }

        //シリアル通信受取ったときの割り込みで呼ばれる関数。
        public void mca_integration(int[] mca_hist)
        {
            for(int i = 0; i < mca_resolution; i++)
            {
                mca_total_buf[i] += mca_hist[i];
            }
            
        }


        //シリアル通信受取ったときの割り込みで呼ばれる関数。
        public void mca_thread_function(int shadow_cnt)
        {
           

            switch (shadow_cnt)
            {
                case (0):
                    mca_result0 = new int[short_buf.Length /  STM32_ADC_OnePulseNum];
                    mca_hist0 = new int[mca_resolution];
                    MCA_Data_Organizer(mca_result0, mca_shortbuf_shadow0, mca_shortbuf_shadow0.Length,  STM32_ADC_OnePulseNum);
                    Histgram_Data_Organizer(mca_hist0, mca_result0);
                    mca_integration(mca_hist0);
                    break;
                case (1):
                    mca_result1 = new int[short_buf.Length /  STM32_ADC_OnePulseNum];
                    mca_hist1 = new int[mca_resolution];
                    MCA_Data_Organizer(mca_result1, mca_shortbuf_shadow1, mca_shortbuf_shadow1.Length,  STM32_ADC_OnePulseNum);
                    Histgram_Data_Organizer(mca_hist1, mca_result1);
                    mca_integration(mca_hist1);
                    break;
                case (2):
                    mca_result2 = new int[short_buf.Length /  STM32_ADC_OnePulseNum];
                    mca_hist2 = new int[mca_resolution];
                    MCA_Data_Organizer(mca_result2, mca_shortbuf_shadow2, mca_shortbuf_shadow2.Length,  STM32_ADC_OnePulseNum);
                    Histgram_Data_Organizer(mca_hist2, mca_result2);
                    mca_integration(mca_hist2);
                    break;
            }
        }
      




        private void Interval_function(object sender, EventArgs e)
        {
            if(mca_flag == 1)
            {
                mca_chart_update(mca_total_buf);
            }
        }


        private void button_mca_clear_Click(object sender, EventArgs e)
        {
            for(int i = 0; i < mca_total_buf.Length; i++)
            {
                mca_total_buf[i] = 0;
            }

            for(int j =0; j < mca_hist_adjusted.Length; j++)
            {
                mca_hist_adjusted[j] = 0;
            }

            mca_chart_update(mca_total_buf);
            sw.Reset();

        }

        private void btn_rx_buf_clear_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
            {
                int buf_left = serialPort1.BytesToRead;
                serialPort1.DiscardInBuffer();

                MessageBox.Show(buf_left.ToString() + "bytes is deleted");
            }
            else
            {
                MessageBox.Show("シリアルポートは閉じています。");
            }
            
        }

        private void button_csv_out_Click(object sender, EventArgs e)
        {

            save_mca_csv_file();

        }

        private void save_mca_csv_file()
        {

            //SaveFileDialogを生成する
            SaveFileDialog sa = new SaveFileDialog();
            sa.Title = "ファイルを保存する";
            sa.InitialDirectory = @"C:\";
            sa.FileName = @"mca_data.csv";
            //sa.Filter = "テキストファイル(*.txt;*.text)|*.txt;*.text|すべてのファイル(*.*)|*.*";
            sa.FilterIndex = 1;
            DialogResult result = sa.ShowDialog();

            if (result == DialogResult.OK)
            {
                //「保存」ボタンが押された時の処理
                string fileName = sa.FileName;  //こんな感じで指定されたファイルのパスが取得できる
                StreamWriter file = new StreamWriter(fileName, false, Encoding.UTF8);
                for (int i = 0; i < mca_total_buf.Length; i++)
                {
                    file.WriteLine(string.Format("{0},{1}", i, mca_total_buf[i]));
                }

                file.Close();

                MessageBox.Show("csvファイルを出力しました。");

            }
            else if (result == DialogResult.Cancel)
            {
                //「キャンセル」ボタンまたは「×」ボタンが選択された時の処理
            }
        }



        private void textBox_threshold_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar < '0' || '9' < e.KeyChar)
            {
                // only number is allowed to type
                e.Handled = true;
            }
        }

        private void textBox_pulse_num_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar < '0' || '9' < e.KeyChar )
            {
                // only number is allowed to type
                e.Handled = true;
            }

            
        }

        private void clear_thresh_btn_Click(object sender, EventArgs e)
        {
            textBox_threshold.Clear();
        }
        private void clear_one_samplenum_btn_Click(object sender, EventArgs e)
        {
            textBox_pulse_num.Clear();
        }


        private async void button_mcu_setting_on_Click(object sender, EventArgs e)
        {
            int threshold_send_value = Convert.ToInt32(textBox_threshold.Text);
            if(threshold_send_value > 4095 || threshold_send_value == 0) {
                MessageBox.Show("閾値が正しくありません。");
                return;
            };

            int one_pulse_sample_num = Convert.ToInt32(textBox_pulse_num.Text);
            if(one_pulse_sample_num == 0) {
                MessageBox.Show("1パルスのサンプル数が正しくありません。");
                return;
            };

           

        }

        private void Send_ADCSettingCodetoSTM32()
        {
            int threshold_send_value = Convert.ToInt32(textBox_threshold.Text);
            if (threshold_send_value > 4095 || threshold_send_value == 0)
            {
                MessageBox.Show("閾値が正しくありません。");
                return;
            };

            int one_pulse_sample_num = Convert.ToInt32(textBox_pulse_num.Text);
            if (one_pulse_sample_num == 0)
            {
                MessageBox.Show("1パルスのサンプル数が正しくありません。");
                return;
            };

            if (serialPort1.IsOpen)
            {
                serialPort1.Write("G" + threshold_send_value.ToString("0000") + one_pulse_sample_num.ToString("0000")); //設定するときのコード　1byte目:"G",  2-5byte目:閾値を文字列4byte（例"0500"）,6-9byte目: 一つのパルスあたりのデータ数を文字列4byte

            }

        }

        private void button_autoopen_Click(object sender, EventArgs e)
        {
            MySerialPort sp_ = new MySerialPort(serialPort1);
            AutoSelect comAuto = new AutoSelect("stm32f4", sp_);

            if (serialPort1.IsOpen == false)
            {
                MessageBox.Show("STM32が見つかりません。");
                return;
            }


            buf_settings(1000); //データ受取り数を1000byteに設定(500データ)

            button_close.Enabled = true;
            button_adcStart.Enabled = true;
            button_adcStop.Enabled = true;
            button_MCA_ON.Enabled = true;
            button_MCA_OFF.Enabled = true;

            button_autoopen.Enabled = false;

        }

        private void button_close_Click(object sender, EventArgs e)
        {
            serialPort1.Write(adc_stop_code);

            serialPort1.DiscardInBuffer();

            serialPort1.Close();

            button_close.Enabled = false;
            button_adcStart.Enabled = false;
            button_adcStop.Enabled = false;
            button_MCA_ON.Enabled = false;
            button_MCA_OFF.Enabled = false;

            button_autoopen.Enabled = true;
        }

        private void button_adcStart_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
            {
                serialPort1.Write(adc_start_code);
            }
            else
            {
                MessageBox.Show("シリアルポートが開いていません。");
            }
        }

        private void button_adcStop_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
            {
                serialPort1.Write(adc_stop_code);
            }
            else
            {
                MessageBox.Show("シリアルポートが開いていません。");
            }
        }

        private void textBox_threshold_MouseHover(object sender, EventArgs e)
        {
            label_adc_threshold_dis.Visible = true;
        }

        private void textBox_threshold_MouseLeave(object sender, EventArgs e)
        {
            label_adc_threshold_dis.Visible = false;
        }

        private void button_MCA_ON_Click(object sender, EventArgs e)
        {
            button_MCA_ON.Enabled = false;
            button_MCA_OFF.Enabled = true;
            button_csv_out.Enabled = true;

            mca_flag = 1;
            chart_MCA_init();
            //mca_integration_init();

            sw.Start();
            timer_stopwatch.Enabled = true;

        }

        private void button_MCA_OFF_Click(object sender, EventArgs e)
        {
            button_MCA_ON.Enabled = true;
            button_MCA_OFF.Enabled = false;

            mca_flag = 0;

            sw.Stop();
            timer_stopwatch.Enabled = false;
        }

        private void button_adcsetting1_Click(object sender, EventArgs e)
        {
            Send_ADCSettingCodetoSTM32();
        }

        private void button_adcsetting2_Click(object sender, EventArgs e)
        {
            Send_ADCSettingCodetoSTM32();
        }

        private void timer_stopwatch_Tick(object sender, EventArgs e)
        {

            int mca_time_seconds =(int) sw.ElapsedMilliseconds / 1000;
            span = new TimeSpan(0,0,mca_time_seconds);
            
            label_TotalCountingTime.Text = span.ToString(@"hh\:mm\:ss");
            
        }
    }

    ////////////////////////////
    //////////////////////////////
    ////////いらないコードたち
    ///



    //private void button_open_Click(object sender, EventArgs e)
    //{

    //    b_rate = comboBox_baudrate.SelectedIndex;
    //    switch (b_rate)
    //    {
    //        case (0): b_rate = 115200; break;
    //        case (1): b_rate = 9600; break;
    //    }

    //    rx_setting = comboBox_rxsetting.SelectedIndex;
    //    switch (rx_setting)
    //    {
    //        case (0): rx_setting = 0; break;
    //        case (1): rx_setting = 1; break;
    //        case (2): rx_setting = 2; break;

    //    }

    //    buf_thresh_setting = comboBox_BufThresh.SelectedIndex;
    //    switch(buf_thresh_setting)
    //    {
    //        case (0):buf_thresh_setting = 1;break;
    //        case (1): buf_thresh_setting = 200; break;
    //        case (2): buf_thresh_setting = 500; break;
    //        case (3): buf_thresh_setting = 1000; break;
    //        case (4): buf_thresh_setting = 2000;break;
    //        case (5):buf_thresh_setting = 10000;break;
    //    }

    //    buf_etc_setting(buf_thresh_setting);


    //    serialPort1.BaudRate = b_rate;
    //    serialPort1.Parity = Parity.None;
    //    serialPort1.DataBits = 8;
    //    serialPort1.StopBits = StopBits.One;
    //    serialPort1.Handshake = Handshake.None;
    //    serialPort1.PortName = "COM11";
    //    serialPort1.ReceivedBytesThreshold = buf_thresh_setting;
    //    serialPort1.Open();


    //    //receive_buffer reset
    //    serialPort1.DiscardInBuffer();

    //    //disable btn or etc


    //    btn_rx_buf_clear.Enabled = true;
    //    btn_rx_buf_clear.Visible = true;

    //    comboBox_baudrate.Enabled = false;
    //    comboBox_BufThresh.Enabled = false;
    //    comboBox_rxsetting.Enabled = false;


    //}


    //private void button3_Click(object sender, EventArgs e)
    //{
    //    //string a = comboBox_cmd.SelectedItem.ToString();
    //    string send_code="B";
    //    switch (a)
    //    {
    //        case "Hello":
    //            send_code = "E";
    //                break;
    //        case "LED_TOGGLE":
    //            send_code = "B";
    //            break;
    //        case "ADC_ON":
    //            send_code = "C";
    //            break;
    //        case "ADC_OFF":
    //            send_code = "D";
    //            break;
    //        case "SINGLE_ADC":
    //            send_code = "F";
    //            break;
    //        case "setting":
    //            send_code = "G";
    //            break;



    //    }

    //    if (serialPort1.IsOpen)
    //    {
    //        serialPort1.Write(send_code);
    //    }
    //}

    //public void buf_etc_setting(int buf_size)
    //{
    //    data_num = buf_size;
    //    receive_buf = new byte[buf_size];
    //    if (buf_size % 2 == 0)
    //    {
    //        short_buf = new short[buf_size / 2];
    //    }
    //}

    //public void mca_result_init(int mca_data_num )
    //{
    //    mca_result0 = new int[mca_data_num];
    //    mca_result1 = new int[mca_data_num];
    //    mca_result2 = new int[mca_data_num];
    //}
}
