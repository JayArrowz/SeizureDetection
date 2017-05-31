using SeizureDetection.CallBatchExecutionService;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SeizureDetection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Instance
        /// </summary>
        public static MainWindow Instance
        {
            get; set;
        }


        /// <summary>
        /// Log list
        /// </summary>
        public ObservableCollection<String> LogErrorList
        {
            get; set;
        }

        public MainWindow()
        {
            Instance = this;
            LogErrorList = new ObservableCollection<String>();
            InitializeComponent();
            this.LogMessageBox.ItemsSource = LogErrorList;
            this.DataContext = this;
        }

        /// <summary>
        /// Log Message
        /// </summary>
        /// <param name="msg"></param>
        public static void Log(string msg)
        {

            App.Current.Dispatcher.Invoke((Action)delegate
            {
                try
                {
                    MainWindow.Instance.LogErrorList.Add("[" + DateTime.Now + "]: " + msg);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            });
        }

        /// <summary>
        /// Log Message
        /// </summary>
        /// <param name="msg"></param>
        public static void SetString(string msg)
        {

            App.Current.Dispatcher.Invoke((Action)delegate
            {
                try
                {
                    MainWindow.Instance.PreSeizureLabel.Content = msg;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            });
        }

        private string[] FilePaths = new string[3];
        public static bool Running = false;

        private void button_Click(object sender, RoutedEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();



            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".csv";
            dlg.Filter = "CSV Files (*.csv)|*.csv";


            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = System.IO.Path.GetFullPath(dlg.FileName);
                FilePaths[0] = filename;
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();



            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".csv";
            dlg.Filter = "CSV Files (*.csv)|*.csv";


            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = System.IO.Path.GetFullPath(dlg.FileName);
                FilePaths[1] = filename;
            }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            // Create OpenFileDialog 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();



            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".csv";
            dlg.Filter = "CSV Files (*.csv)|*.csv";


            // Display OpenFileDialog by calling ShowDialog method 
            Nullable<bool> result = dlg.ShowDialog();


            // Get the selected file name and display in a TextBox 
            if (result == true)
            {
                // Open document 
                string filename = System.IO.Path.GetFullPath(dlg.FileName);
                FilePaths[2] = filename;
            }
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            if(FilePaths[0] == null)
            {
                MessageBox.Show("Please input seizure data.");
                return;
            }

            if(FilePaths[1] == null)
            {
                MessageBox.Show("Please input non-seizure data.");
                return;
            }



            if (FilePaths[2] == null)
            {
                MessageBox.Show("Please input data to be classified.");
                return;
            }
            if(Running)
            {
                MessageBox.Show("An operation is already running.");
                return;
            }
            Running = true;
            AzureMLConnection.Run(FilePaths);
        }
    }
}
