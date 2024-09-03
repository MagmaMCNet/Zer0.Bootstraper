using Microsoft.Win32;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Bootstraper
{
    public partial class PathList : UserControl
    {
        public PathList()
        {
            InitializeComponent();
        }

        public void AddItem(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !PathListBox.Items.Contains(path))
            {
                PathListBox.Items.Add(path);
            }
        }

        public void AddItem()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "All Files|*.*",
                Multiselect = true
            };
            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var filename in openFileDialog.FileNames)
                {
                    AddItem(filename);
                }
            }
        }

        public void RemoveItem(string path)
        {
            PathListBox.Items.Remove(path);
        }

        public void RemoveItem(ushort index)
        {
            if (index < PathListBox.Items.Count)
            {
                PathListBox.Items.RemoveAt(index);
            }
        }

        public List<string> GetList()
        {
            var items = new List<string>();
            foreach (var item in PathListBox.Items)
            {
                items.Add(item.ToString());
            }
            return items;
        }

        public bool ValidateItems()
        {
            foreach (var item in PathListBox.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ToString()) || !System.IO.File.Exists(item.ToString()))
                {
                    return false;
                }
            }
            return true;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddItem();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string path = button.Tag.ToString();
            RemoveItem(path);
        }
    }
}
