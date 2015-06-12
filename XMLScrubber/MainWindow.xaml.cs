using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MahApps.Metro;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace XMLScrubber
{
    //XML Scrubber
    //Eduardo Martinez
    //4-2-2015
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        //Background Worker
        private readonly BackgroundWorker worker = new BackgroundWorker();
        //Delegate Open File Method
        private delegate void DelegateOpenFile(String s);
        DelegateOpenFile openFileDelegate;
        //Tooltip
        ToolTip toolTip = new ToolTip();
        //Exanded Grid
        GridLength[] starHeight;

        //Daclarations
        string _xmlpath;
        FoldingManager foldingManager;
        AbstractFoldingStrategy foldingStrategy;

        public MainWindow()
        {
            Loaded += MyWindow_Loaded;
            InitializeComponent();
            //Percentage.Content = String.Empty;
            this.AllowDrop = true;
            openFileDelegate = new DelegateOpenFile(this.OpenFile);
            var typeConverter = new HighlightingDefinitionTypeConverter();
            var xmlSyntaxHighlighter = (IHighlightingDefinition)typeConverter.ConvertFrom("XML");
            xmlin.SyntaxHighlighting = xmlSyntaxHighlighter;
            xmlin.ShowLineNumbers = true;
            DispatcherTimer foldingUpdateTimer = new DispatcherTimer();
            foldingUpdateTimer.Interval = TimeSpan.FromSeconds(2);
            foldingUpdateTimer.Tick += foldingUpdateTimer_Tick;
            foldingUpdateTimer.Start();
            xmlin.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            xmlin.FontSize = 13.333333333333333; // 10pt
            TextOptions.SetTextFormattingMode(xmlin, TextFormattingMode.Ideal);
            SolidColorBrush color = new SolidColorBrush(Color.FromRgb(243, 243, 243));
            xmlin.Background = color;

            //Expander
            starHeight = new GridLength[expanderGrid.RowDefinitions.Count];
            starHeight[0] = expanderGrid.RowDefinitions[0].Height;
            starHeight[2] = expanderGrid.RowDefinitions[2].Height;
            ExpandedOrCollapsed(topExpander);
            ExpandedOrCollapsed(bottomExpander);

            // InitializeComponent calls topExpander.Expanded
            topExpander.Expanded += ExpandedOrCollapsed;
            topExpander.Collapsed += ExpandedOrCollapsed;
            bottomExpander.Expanded += ExpandedOrCollapsed;
            bottomExpander.Collapsed += ExpandedOrCollapsed;

            // Create the events for the Background Worker.
            if (worker.IsBusy != true)
            {
                worker.WorkerReportsProgress = true;
                worker.WorkerSupportsCancellation = true;
                worker.DoWork += new DoWorkEventHandler(worker_DoWork);
                worker.ProgressChanged += new ProgressChangedEventHandler(worker_ProgressChanged);
                worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);
            }
        }

        //Load Window
        private void MyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var theme = ThemeManager.DetectAppStyle(Application.Current);
            // Set the Green accent and dark theme
            ThemeManager.ChangeAppStyle(Application.Current,
                                        ThemeManager.GetAccent("Green"),
                                        ThemeManager.GetAppTheme("BaseDark"));
            //Replace.IsSelected = true;
            txtFind2.Focus();
            Restore.Visibility = System.Windows.Visibility.Hidden;
            MouseDown += delegate { DragMove(); };           
        }

        //Background Worker Do Work
        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            //Node list from agrument passed to RunWorkerAsync
            XmlNodeList node = e.Argument as XmlNodeList;
            for (int i = 0; i < node.Count; i++)
            {
                worker.ReportProgress(Convert.ToInt32(i * 100 / node.Count));
            }
            XPathDocument docpath = new XPathDocument(xmlpath);
            XPathNavigator nav = docpath.CreateNavigator();
            XPathNodeIterator itor = (XPathNodeIterator)nav.Evaluate("/*");
            //Dispatcher for UI Thread
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => displayNavigator(itor)));
        }

        //Background Worker Progress Changed
        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;
        }

        //Background Worker Completed
        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progressBar.Value = 100;
        }

        //Drag Window
        public void DragWindow(object sender, MouseButtonEventArgs args)
        {
            DragMove();
        }

        //Folding Timer
        void foldingUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (foldingStrategy != null)
            {
                foldingStrategy.UpdateFoldings(foldingManager, xmlin.Document);
            }
        }

        //Close Window
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        //Minimize Window
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        //Restore Down Window
        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                Maximize.Visibility = System.Windows.Visibility.Visible;
                Restore.Visibility = System.Windows.Visibility.Hidden;
            }
        }

        //Maximize Window
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Maximized;
            Maximize.Visibility = System.Windows.Visibility.Hidden;
            Restore.Visibility = System.Windows.Visibility.Visible;
        }

        //Drop Events
        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    openFileDelegate(files[0]);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error in DragDrop function: " + ex.Message);
            }
        }

        //Declare the OnPropertyChanged Event 
        public event PropertyChangedEventHandler PropertyChanged;
        private void RaisePropertyChanged(string prop)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }

        //Raise Event when XML Path Changes using Interface INotifyPropertyChanged
        public string xmlpath
        {
            get
            {
                return this._xmlpath;
            }
            set
            {
                if (_xmlpath != value)
                {
                    _xmlpath = value;
                    RaisePropertyChanged("xmlpath");
                    progressBar.Value = 0;
                }
            }
        }

        //OpenFile Method for DragDrop Events
        private void OpenFile(string sFile)
        {
            txtXMLPath.Text = System.IO.Path.GetFileName(sFile);
            if (!sFile.Contains(".xml"))
            {
                MessageBox.Show(sFile + " Is Not An XML File!");
            }
            else if (sFile == xmlpath)
            {
                MessageBox.Show(sFile + " Is Already Open!");
            }
            else
            {
                xmlpath = sFile.ToString();
                txtXMLPath.ToolTip = "Full Path: " + xmlpath;
                LoadXml(xmlpath);
                foldingStrategy = new XmlFoldingStrategy();
                if (foldingStrategy != null)
                {
                    if (foldingManager == null)
                        foldingManager = FoldingManager.Install(xmlin.TextArea);
                    foldingStrategy.UpdateFoldings(foldingManager, xmlin.Document);
                }
                else
                {
                    if (foldingManager != null)
                    {
                        FoldingManager.Uninstall(foldingManager);
                        foldingManager = null;
                    }
                }
            }
        }

        //Reload Button
        private void btnReload_Click(object sender, RoutedEventArgs e)
        {
            txtFind.Text = String.Empty;
            txtFind2.Text = String.Empty;
            txtReplace.Text = String.Empty;
            txtEval.Text = String.Empty;
            txtValue.Text = String.Empty;
            tvxpath.Items.Clear();
            if (xmlin.Text != string.Empty)
            {
                LoadXml(xmlpath);
                foldingStrategy = new XmlFoldingStrategy();
                if (foldingStrategy != null)
                {
                    if (foldingManager == null)
                        foldingManager = FoldingManager.Install(xmlin.TextArea);
                    foldingStrategy.UpdateFoldings(foldingManager, xmlin.Document);
                }
                else
                {
                    if (foldingManager != null)
                    {
                        FoldingManager.Uninstall(foldingManager);
                        foldingManager = null;
                    }
                }
            }
        }

        //Explorer Button
        private void btnExplorer_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Title = "Open XML Files";
            openFileDialog1.DefaultExt = "xml";
            openFileDialog1.Filter = "XML Files (*.xml)|*.xml";
            openFileDialog1.FilterIndex = 2;
            openFileDialog1.RestoreDirectory = true;
            if (openFileDialog1.ShowDialog() == true)
            {
                try
                {
                    string dbfilename = openFileDialog1.FileName;
                    openFileDelegate(dbfilename);
                }

                catch (Exception ex)
                {
                    txtmessage.Text = ex.Message;
                }
            }
        }

        //Find Next Button
        private void FindNextClick(object sender, RoutedEventArgs e)
        {
            if (!FindNext(txtFind.Text))
                SystemSounds.Beep.Play();
        }

        //Find Next2 Button
        private void FindNext2Click(object sender, RoutedEventArgs e)
        {
            if (!FindNext(txtFind2.Text))
                SystemSounds.Beep.Play();
        }

        //Replace Button
        private void ReplaceClick(object sender, RoutedEventArgs e)
        {
            Regex regex = GetRegEx(txtFind2.Text);
            string input = xmlin.Text.Substring(xmlin.SelectionStart, xmlin.SelectionLength);
            Match match = regex.Match(input);
            bool replaced = false;
            if (match.Success && match.Index == 0 && match.Length == input.Length)
            {
                xmlin.Document.Replace(xmlin.SelectionStart, xmlin.SelectionLength, txtReplace.Text);
                replaced = true;
            }
            if (!FindNext(txtFind2.Text) && !replaced)
                SystemSounds.Beep.Play();
        }

        //Replace All Button
        private void ReplaceAllClick(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to Replace All occurences of \"" +
            txtFind2.Text + "\" with \"" + txtReplace.Text + "\"?",
                "Replace All", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            {
                Regex regex = GetRegEx(txtFind2.Text, true);
                int offset = 0;
                xmlin.BeginChange();
                foreach (Match match in regex.Matches(xmlin.Text))
                {
                    xmlin.Document.Replace(offset + match.Index, match.Length, txtReplace.Text);
                    offset += txtReplace.Text.Length - match.Length;
                }
                xmlin.EndChange();
            }
        }

        //Find Next Button
        private bool FindNext(string textToFind)
        {
            Regex regex = GetRegEx(textToFind);
            int start = regex.Options.HasFlag(RegexOptions.RightToLeft) ?
            xmlin.SelectionStart : xmlin.SelectionStart + xmlin.SelectionLength;
            Match match = regex.Match(xmlin.Text, start);

            if (!match.Success)  // start again from beginning or end
            {
                if (regex.Options.HasFlag(RegexOptions.RightToLeft))
                    match = regex.Match(xmlin.Text, xmlin.Text.Length);
                else
                    match = regex.Match(xmlin.Text, 0);
            }
            if (match.Success)
            {
                xmlin.Select(match.Index, match.Length);
                TextLocation loc = xmlin.Document.GetLocation(match.Index);
                xmlin.ScrollTo(loc.Line, loc.Column);
            }
            return match.Success;
        }

        //Evaluate Button
        private void Eval_Click(object sender, RoutedEventArgs e)
        {
            if (xmlin.Text != string.Empty)
            {
                try
                {
                    XmlDocument xdoc = new XmlDocument();
                    xdoc.LoadXml(xmlin.Text);
                    txtValue.Text = (xdoc.SelectSingleNode(txtEval.Text.Trim()).Value);
                }
                catch (Exception ex)
                {
                    txtmessage.Text = ex.ToString();
                }
            }
        }

        //Regular Expression Search
        private Regex GetRegEx(string textToFind, bool leftToRight = false)
        {
            RegexOptions options = RegexOptions.None;
            if (cbSearchUp.IsChecked == true && !leftToRight)
                options |= RegexOptions.RightToLeft;
            if (cbCaseSensitive.IsChecked == false)
                options |= RegexOptions.IgnoreCase;

            if (cbRegex.IsChecked == true)
            {
                return new Regex(textToFind, options);
            }
            else
            {
                string pattern = Regex.Escape(textToFind);
                if (cbWildcards.IsChecked == true)
                    pattern = pattern.Replace("\\*", ".*").Replace("\\?", ".");
                if (cbWholeWord.IsChecked == true)
                    pattern = "\\b" + pattern + "\\b";
                return new Regex(pattern, options);
            }
        }

        //Find Replace Method
        private static MainWindow theDialog = null;
        private void FindReplaceDialog(TextEditor editor)
        {
            string textToFind = "";
            bool caseSensitive = false;
            bool wholeWord = true;
            bool useRegex = false;
            bool useWildcards = false;
            bool searchUp = false;
            editor = xmlin;
            txtFind.Text = txtFind2.Text = textToFind;
            cbCaseSensitive.IsChecked = caseSensitive;
            cbWholeWord.IsChecked = wholeWord;
            cbRegex.IsChecked = useRegex;
            cbWildcards.IsChecked = useWildcards;
            cbSearchUp.IsChecked = searchUp;
        }

        //Show for Replace Method
        private void ShowForReplace(TextEditor editor)
        {
            if (theDialog == null)
            {
                theDialog.tabMain.SelectedIndex = 1;
                theDialog.Show();
                theDialog.Activate();
            }
            else
            {
                theDialog.tabMain.SelectedIndex = 1;
                theDialog.Activate();
            }

            if (!editor.TextArea.Selection.IsMultiline)
            {
                theDialog.txtFind.Text = theDialog.txtFind2.Text = editor.TextArea.Selection.GetText();
                theDialog.txtFind.SelectAll();
                theDialog.txtFind2.SelectAll();
                theDialog.txtFind2.Focus();
            }
        }

        //Check XML File Integrity
        public bool IsValidXml(string xmlfile)
        {
            try
            {
                XDocument xd1 = new XDocument();
                xd1 = XDocument.Load(xmlfile);
                return true;
            }
            catch (XmlException ex)
            {
                MessageBox.Show("XML File Is Probably Bad..." + ex.Message);
                return false;
            }
        }

        //Load XML File
        private void LoadXml(string xmlpath)
        {
            tvxpath.Items.Clear();
            xmlin.Text = string.Empty;
            if (!File.Exists(xmlpath))
            {
                txtmessage.Text = xmlpath + " does not exist.";
                return;
            }
            if (IsValidXml(xmlpath))
            {
                txtmessage.Text = string.Empty;
                XmlDocument doc = new XmlDocument();
                doc.Load(xmlpath);
                XmlNodeList node = doc.SelectNodes("//*");
                if (worker.IsBusy != true)
                {
                    worker.RunWorkerAsync(node);
                }
            }
        }

        //Load TreeView
        private void displayNavigator(XPathNodeIterator xpi)
        {
            if ((xpi != null) && (xpi.Count > 0))
            {
                for (bool hasNext = xpi.MoveNext(); hasNext; hasNext = xpi.MoveNext())
                {
                    // IXmlLineInfo lineInfo = xpi.Current as IXmlLineInfo;
                    switch (xpi.Current.NodeType)
                    {
                        case XPathNodeType.Text:
                            {
                                TreeViewItem node = new TreeViewItem();
                                node.Header = xpi.Current.Value;
                                node.Foreground = Brushes.Brown;
                                node.ToolTip = "(Nodeset/Text)";
                                tvxpath.Items.Add(node);
                                break;
                            }
                        case XPathNodeType.Attribute:
                            {
                                TreeViewItem node = new TreeViewItem();
                                node.Header = "@" + xpi.Current.Name + ": " + xpi.Current.Value;
                                node.Foreground = Brushes.Brown;
                                node.ToolTip = "(Nodeset/Attribute)";
                                node.Tag = xpi.Current.Clone();
                                tvxpath.Items.Add(node);
                                break;
                            }
                        case XPathNodeType.Element:
                            {
                                var document = new XSDocument(xpi.Current.OuterXml);
                                ViewerNode vNode = new ViewerNode(document);
                                TreeViewItem tvi = TreeViewHelper.BuildTreeView(vNode);
                                tvxpath.Items.Add(tvi);
                                break;
                            }
                    }
                    if (string.IsNullOrEmpty(xmlin.Text))
                    {
                        xmlin.Text = xpi.Current.OuterXml;
                    }
                    else
                    {
                        xmlin.Text = xmlin.Text + "\r\n" + xpi.Current.OuterXml;
                    }
                }
            }
            else
            {
                tvxpath.Items.Add("Nothing found.");
                xmlin.Text = "";
            }
        }

        void tvxpath_SelectionChanged(ViewerNode obj)
        {
            if (obj == null)
            {
                return;
            }
            highlightFragment(obj);
        }

        private void selectNodeBasedOnCursor()
        {
            //_updateSelectedBasedOnCursorNeccessary = false;
            try
            {
                Cursor = Cursors.Wait;
                var loc = xmlin.TextArea.Caret.Location;
                SelectNodeBasedOnCursor(loc);
            }
            finally
            {
                Cursor = null;
            }
        }

        public void SelectNodeBasedOnCursor(TextLocation loc)
        {
            ItemCollection items = tvxpath.Items;
            Debug.Assert(items.Count <= 1);
            if (items.Count != 1)
            {
                return;
            }

            LazyTreeViewItem root = (LazyTreeViewItem)tvxpath.Items[0];
            IEnumerable<LazyTreeViewItem> allChilds = root.AsDepthFirstEnumerable(
                x =>
                {
                    x.Expand();
                    return x.Items.Cast<LazyTreeViewItem>();
                }
                );

            TreeViewItem match = null;
            foreach (LazyTreeViewItem child in allChilds)
            {
                if (child.Tag != null)
                {
                    ViewerNode node = (ViewerNode)child.Tag;
                    IXmlLineInfo lineInfo = node.LineInfo;
                    if (lineInfo != null)
                    {
                        if (lineInfo.LineNumber == loc.Line && lineInfo.LinePosition <= loc.Column)
                        {
                            //last one counts
                            match = child;
                        }
                        if (lineInfo.LineNumber > loc.Line || (lineInfo.LineNumber == loc.Line && lineInfo.LinePosition > loc.Column))
                        {
                            break;
                        }
                    }
                }
            }
            if (match != null)
            {
                tvxpath.SelectedItemChanged -= tvxpath_SelectedItemChanged;
                match.IsSelected = true;
                match.BringIntoView();
                tvxpath.SelectedItemChanged += tvxpath_SelectedItemChanged;
            }
        }

        //Set Tag
        private void setTag(TreeViewItem tvi, object newTag)
        {
            tvi.Tag = newTag;
            foreach (TreeViewItem kid in tvi.Items)
            {
                setTag(kid, newTag);
            }
        }

        //TreeView Selected Item
        private void tvxpath_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (tvxpath.SelectedItem == null)
            {
                return;
            }
            TreeViewItem selected = ((TreeViewItem)tvxpath.SelectedItem);
            selected.IsExpanded = true;
            if (selected.Tag != null)
            {
                ViewerNode selectedNode = (ViewerNode)((TreeViewItem)tvxpath.SelectedItem).Tag;
                highlightFragment(selectedNode);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlin.Text);
                XmlNodeList node = doc.SelectNodes("//@" + selectedNode.Name);
                DocumentLine line = xmlin.Document.GetLineByOffset(xmlin.CaretOffset);
                foreach (XmlNode xn in node)
                {
                    if (xn.Value.Contains(selectedNode.Value) && (xn.Name.Contains(selectedNode.Name) && selectedNode.LineInfo.LineNumber == line.LineNumber))
                    {
                        string mynode = GetXPathToNode(xn);
                        txtmessage.Text = mynode;
                        Clipboard.SetDataObject(mynode, true);
                        break;
                    }
                }
            }
        }

        private void highlightFragment(ViewerNode selectedNode)
        {
            if (xmlin == null)
            {
                return;
            }

            //show fragment in fragment view
            if (selectedNode == null)
            {
                //todo
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                XmlWriterSettings settings = new XmlWriterSettings
                {
                    ConformanceLevel = ConformanceLevel.Auto,
                    Indent = true
                };

                using (XmlWriter w = XmlWriter.Create(sb, settings))
                {
                    selectedNode.OriginalNode.WriteTo(w);
                }
            }

            //select node in editor
            if (selectedNode != null)
            {
                IXmlLineInfo lineInfo = selectedNode.LineInfo;
                if (lineInfo != null)
                {
                    var offset = xmlin.Document.GetOffset(lineInfo.LineNumber, lineInfo.LinePosition);
                    xmlin.Select(offset, selectedNode.Name.Length);
                    Debug.Assert(xmlin.SelectedText == selectedNode.Name);
                    xmlin.ScrollTo(lineInfo.LineNumber, lineInfo.LinePosition);
                }
            }
        }

        void ExpandedOrCollapsed(object sender, RoutedEventArgs e)
        {
            ExpandedOrCollapsed(sender as Expander);
        }

        void ExpandedOrCollapsed(Expander expander)
        {
            var rowIndex = Grid.GetRow(expander);
            var row = expanderGrid.RowDefinitions[rowIndex];
            if (expander.IsExpanded)
            {
                row.Height = starHeight[rowIndex];
                row.MinHeight = 50;
            }
            else
            {
                starHeight[rowIndex] = row.Height;
                row.Height = GridLength.Auto;
                row.MinHeight = 0;
            }

            var bothExpanded = topExpander.IsExpanded && bottomExpander.IsExpanded;
            splitter.Visibility = bothExpanded ?
                Visibility.Visible : Visibility.Collapsed;
        }

        // Copy the node text to the clipboard
        private void copyTextToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string tmp = tvxpath.SelectedItem.ToString();
            Clipboard.SetDataObject(tmp, true);
        }

        //Mouse Hover Stopped
        private void xmlin_MouseHoverStopped(object sender, MouseEventArgs e)
        {
            toolTip.IsOpen = false;
        }

        //Get Text Under Mouser Hover
        private void xmlin_MouseHover(object sender, MouseEventArgs e)
        {
            int line = 0;
            if ((xmlin.Text != string.Empty))
            {
                var pos = xmlin.GetPositionFromPoint(e.GetPosition(xmlin));
                if (pos != null)
                {
                    string wordHovered = xmlin.Document.GetWordUnderMouse(pos.Value);
                    string ext = wordHovered.Substring(0, wordHovered.LastIndexOf("=") + 1);
                    string trimmed = ext.TrimEnd('=');
                    e.Handled = true;
                    try
                    {
                        XmlDocument doc = new XmlDocument();
                        //doc.PreserveWhitespace = true;
                        doc.LoadXml(xmlin.Text);
                        string searchLeft = wordHovered.ToString().Substring(0, wordHovered.ToString().IndexOf("="));
                        string searchRight = wordHovered.ToString().Substring(wordHovered.ToString().LastIndexOf('=') + 1);
                        XmlNodeList node = doc.SelectNodes("//@" + searchLeft);
                        XPathDocument xdoc = new XPathDocument(xmlpath);
                        foreach (XmlNode xn in node)
                        {
                            foreach (XPathNavigator element in xdoc.CreateNavigator().Select(GetXPathToNode(xn)))
                            {
                                line = (IXmlLineInfo)element != null ? ((IXmlLineInfo)element).LineNumber : 0;
                            }
                            if (xn.Value.Contains(searchRight.Replace("\"", "")) && pos.Value.Line.Equals(line - 1))
                            {
                                string mynode = GetXPathToNode(xn);
                                txtmessage.Text = mynode;
                                Clipboard.SetDataObject(mynode, true);
                                toolTip.PlacementTarget = this; // required for property inheritance
                                //toolTip.Content = pos.ToString();
                                toolTip.Content = mynode.ToString();
                                toolTip.IsOpen = true;
                                break;
                            }
                            //else
                            //    {
                            //    txtmessage.Text = wordHovered + " Is Not a Node";
                            //    }
                        }
                    }
                    catch (Exception ex)
                    {
                        txtmessage.Text = ex.Message;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the X-Path to a given Node
        /// </summary>
        /// <param name="node">The Node to get the X-Path from</param>
        /// <returns>The X-Path of the Node</returns>
        public string GetXPathToNode(XmlNode node)
        {
            if (node.NodeType == XmlNodeType.Attribute)
            {
                // attributes have an OwnerElement, not a ParentNode; also they have             
                // to be matched by name, not found by position             
                return String.Format("{0}/@{1}", GetXPathToNode(((XmlAttribute)node).OwnerElement), node.Name);
            }
            if (node.ParentNode == null)
            {
                // the only node with no parent is the root node, which has no path
                return "";
            }

            // Get the Index
            int indexInParent = 1;
            XmlNode siblingNode = node.PreviousSibling;
            // Loop thru all Siblings
            while (siblingNode != null)
            {
                // Increase the Index if the Sibling has the same Name
                if (siblingNode.Name == node.Name)
                {
                    indexInParent++;
                }
                siblingNode = siblingNode.PreviousSibling;
            }
            // the path to a node is the path to its parent, plus "/node()[n]", where n is its position among its siblings.         
            return String.Format("{0}/{1}[{2}]", GetXPathToNode(node.ParentNode), node.Name, indexInParent);
        }

        //Save Button
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Title = "Save XML Files";
            dlg.DefaultExt = "xml";
            dlg.Filter = "XML Files (*.xml)|*.xml";
            dlg.RestoreDirectory = true;
            {
                if (dlg.ShowDialog() ?? false)
                {
                    string extension = System.IO.Path.GetExtension(xmlpath);
                    File.WriteAllText(dlg.FileName, xmlin.Text);
                }
            }
        }

        //Clear Buttton
        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            txtEval.Text = string.Empty;
            txtValue.Text = string.Empty;
            txtFind.Text = string.Empty;
            txtFind2.Text = string.Empty;
            txtReplace.Text = string.Empty;
        }

        //CheckBoxes
        private void cbCollapse_Checked(object sender, RoutedEventArgs e)
        {
            if (xmlin.Text != string.Empty)
            {
                foreach (FoldingSection fm in foldingManager.AllFoldings)
                {
                    fm.IsFolded = true;
                }

            }
        }

        private void cbCollapse_UnChecked(object sender, RoutedEventArgs e)
        {
            if (xmlin.Text != string.Empty)
            {
                foreach (FoldingSection fm in foldingManager.AllFoldings)
                {
                    fm.IsFolded = false;
                }
            }
        }
        private void cbWholeWord_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void cbCaseSensitive_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void cbRegex_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void cbWildcards_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void cbSearchUp_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void progressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }
    }
}

//AvalonEdit Extensions
public static class AvalonEditExtensions
{
    public static string GetWordUnderMouse(this TextDocument document, TextViewPosition position)
    {
        string wordHovered = string.Empty;
        var line = position.Line;
        var column = position.Column;
        var offset = document.GetOffset(line, column);
        if (offset >= document.TextLength)
            offset--;
        var textAtOffset = document.GetText(offset, 1);

        // Get text backward of the mouse position, until the first space
        while (!string.IsNullOrWhiteSpace(textAtOffset))
        {
            wordHovered = textAtOffset + wordHovered;
            offset--;
            if (offset < 0)
                break;
            textAtOffset = document.GetText(offset, 1);
        }

        // Get text forward the mouse position, until the first space
        offset = document.GetOffset(line, column);
        if (offset < document.TextLength - 1)
        {
            offset++;
            textAtOffset = document.GetText(offset, 1);
            while (!string.IsNullOrWhiteSpace(textAtOffset))
            {
                wordHovered = wordHovered + textAtOffset;
                offset++;
                if (offset >= document.TextLength)
                    break;
                textAtOffset = document.GetText(offset, 1);
            }
        }
        return wordHovered;
    }

    public static string GetWordBeforeDot(this TextEditor textEditor)
    {
        var wordBeforeDot = string.Empty;
        var caretPosition = textEditor.CaretOffset - 2;
        var lineOffset = textEditor.Document.GetOffset(textEditor.Document.GetLocation(caretPosition));
        string text = textEditor.Document.GetText(lineOffset, 1);

        // Get text backward of the mouse position, until the first space
        while (!string.IsNullOrWhiteSpace(text) && text.CompareTo(".") > 0)
        {
            wordBeforeDot = text + wordBeforeDot;
            if (caretPosition == 0)
                break;
            lineOffset = textEditor.Document.GetOffset(textEditor.Document.GetLocation(--caretPosition));

            text = textEditor.Document.GetText(lineOffset, 1);
        }
        return wordBeforeDot;
    }
}