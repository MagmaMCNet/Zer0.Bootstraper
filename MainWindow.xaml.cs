using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Application = System.Windows.Application;
using System.Diagnostics;

namespace Bootstraper
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LoadPaths();
            LoadDependencies();
        }

        private void LoadPaths()
        {
            MainExe.Text = LoadFromRegistry("MainExe") ?? MainExe.Text;
            OutputExe.Text = LoadFromRegistry("OutputExe") ?? OutputExe.Text;
            IconPath.Text = LoadFromRegistry("IconPath") ?? IconPath.Text;
            UseGZip.IsChecked = (LoadFromRegistry("UseGZip") ?? "true") == "true" ? true : false;
        }

        private void SaveDependencies()
        {
            using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(@"Software\Zer0\Bootstraper\Dependencies"))
            {
                foreach (var val in registryKey.GetValueNames())
                    registryKey.DeleteValue(val);
                var dependencies = Dependencies.GetList();
                foreach (var dependency in dependencies)
                {
                    var base64Dependency = Convert.ToBase64String(Encoding.UTF8.GetBytes(dependency));
                    var randomName = GenerateRandomString(6);
                    registryKey.SetValue(randomName, base64Dependency);
                }
            }
        }
        private void LoadDependencies()
        {
            using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(@"Software\Zer0\Bootstraper\Dependencies"))
            {
                if (registryKey != null)
                {
                    foreach (var dependencyName in registryKey.GetValueNames())
                    {
                        string base64Dependency = registryKey.GetValue(dependencyName)?.ToString();
                        if (!string.IsNullOrEmpty(base64Dependency))
                        {
                            string dependencyPath = Encoding.UTF8.GetString(Convert.FromBase64String(base64Dependency));
                            if (File.Exists(dependencyPath))
                                Dependencies.AddItem(dependencyPath);
                            else
                                registryKey.DeleteValue(dependencyName);
                        }
                    }
                }
            }
        }
        private string GenerateRandomString(ushort length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            using (var rng = new RNGCryptoServiceProvider())
            {
                var data = new byte[length];
                rng.GetBytes(data);
                return new string(data.Select(b => chars[b % chars.Length]).ToArray());
            }
        }

        public void Compile()
        {
            string mainExePath = MainExe.Text;
            string outputExePath = OutputExe.Text;
            string iconPath = IconPath.Text;

            if (string.IsNullOrEmpty(mainExePath) || string.IsNullOrEmpty(outputExePath))
            {
                MessageBox.Show("Please specify both Main EXE and Output EXE paths.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!File.Exists(mainExePath))
            {
                MessageBox.Show("Main EXE file does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BuildButton.IsEnabled = false;
            BuildButton.Content = "Compiling...";
            byte[] mainExeBytes = File.ReadAllBytes(mainExePath);
            var compiler = new BootstrapCompiler();
            compiler.UseGZip = UseGZip.IsChecked ?? true;
            compiler.SetMainExe(Path.GetFileName(mainExePath), mainExeBytes);

            if (!string.IsNullOrEmpty(iconPath))
                if (File.Exists(iconPath))
                    compiler.SetIcon(iconPath);

            SaveDependencies();

            foreach (var dependency in Dependencies.GetList())
            {
                if (File.Exists(dependency))
                {
                    byte[] dependencyBytes = File.ReadAllBytes(dependency);
                    compiler = compiler.AddEmbeddedResources(Path.GetFileName(dependency), dependencyBytes);
                }
                else
                {
                    MessageBox.Show($"Dependency {dependency} does not exist.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            Thread CompileThread = new Thread(() =>
            {
                bool result = compiler.Compile(outputExePath);
                GC.Collect();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(result ? "Build Successful!" : "Build Failed!", "Result", MessageBoxButton.OK, result ? MessageBoxImage.Information : MessageBoxImage.Error);
                    BuildButton.Content = "Build";
                    BuildButton.IsEnabled = true;
                    if (result)
                        Process.Start("explorer.exe", $"/select,\"{outputExePath}\"");
                });

            })
            {
                Priority = ThreadPriority.BelowNormal,
                Name = "Bootstrap Compilation",
                IsBackground = true
            };
            CompileThread.Start();

        }

        private void MainExe_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenFileAndSetPath(MainExe, "EXE files (*.exe)|*.exe|All files (*.*)|*.*");

        private void OutputExe_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            SaveFileAndSetPath(OutputExe, "EXE files (*.exe)|*.exe|All files (*.*)|*.*");

        private void Icon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            OpenFileAndSetPath(IconPath, "Icon files (*.ico)|*.ico|All files (*.*)|*.*");

        private void MainExe_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            MainExe.Text = "Path to Main EXE";
            SaveToRegistry(MainExe.Name, null);
        }
        private void OutputExe_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            OutputExe.Text = "Output EXE Name";
            SaveToRegistry(OutputExe.Name, null);
        }
        private void Icon_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            IconPath.Text = "Path to Icon (optional)";
            SaveToRegistry(IconPath.Name, null);
        }

        private void BuildClick(object sender, RoutedEventArgs e) =>
            Compile();

        private void OpenFileAndSetPath(TextBox textBox, string filter)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = filter
            };

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBox.Text = openFileDialog.FileName;
                SaveToRegistry(textBox.Name, openFileDialog.FileName);
            }
        }

        private void SaveFileAndSetPath(TextBox textBox, string filter)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = "exe"
            };

            if (saveFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBox.Text = saveFileDialog.FileName;
                SaveToRegistry(textBox.Name, saveFileDialog.FileName);
            }
        }

        private void SaveToRegistry(string key, object value)
        {
            using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(@"Software\Zer0\Bootstraper"))
            {
                try
                {
                    if (value == null)
                        registryKey.DeleteValue(key);
                    else
                        registryKey.SetValue(key, value);
                }
                catch { }
            }
        }

        private string LoadFromRegistry(string key)
        {
            using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(@"Software\Zer0\Bootstraper"))
            {
                return registryKey?.GetValue(key)?.ToString();
            }
        }

        private void UseGZip_Checked(object sender, RoutedEventArgs e)
        {
            SaveToRegistry(UseGZip.Name, UseGZip.IsChecked ?? true ? "true" : "false");
        }
    }
}
