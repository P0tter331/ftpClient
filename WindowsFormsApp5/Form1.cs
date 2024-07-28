using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsFormsApp5
{
    public partial class Form1 : Form
    {
        #region Private variable
        private Socket cmdSocket;
        private Socket dataSocket;
        private NetworkStream cmdStrmWtr;
        private StreamReader cmdStrmRdr;
        private NetworkStream dataStrmWtr;
        private StreamReader dataStrmRdr;
        private String cmdData;
        private byte[] szData;
        private const String CRLF = "\r\n";
        private bool isPaused = false;
        private ManualResetEvent pauseEvent = new ManualResetEvent(true);
        #endregion

        #region Private Functions

        private String getSatus()
        {
            byte[] buffer = new byte[1024];
            int bytesRead = cmdSocket.Receive(buffer);
            String ret = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            Invoke((MethodInvoker)(() =>
            {
                lsb_status.Items.Add(ret);
                lsb_status.SelectedIndex = lsb_status.Items.Count - 1;
            }));
            return ret;
        }

        private void openDataPort()
        {
            string retstr;
            string[] retArray;
            int dataPort;

            // Start Passive Mode 
            cmdData = "PASV" + CRLF;
            szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
            cmdSocket.Send(szData);
            retstr = this.getSatus();

            // Calculate data's port
            retArray = Regex.Split(retstr, ",");
            if (retArray[5][2] != ')') retstr = retArray[5].Substring(0, 3);
            else retstr = retArray[5].Substring(0, 2);
            dataPort = Convert.ToInt32(retArray[4]) * 256 + Convert.ToInt32(retstr);
            Invoke((MethodInvoker)(() =>
            {
                lsb_status.Items.Add("Get dataPort=" + dataPort);
            }));

            string IP = tb_IP.Text;

            // Connect to the dataPort
            dataSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            dataSocket.Connect(IP, dataPort);
            dataStrmRdr = new StreamReader(new NetworkStream(dataSocket));
            dataStrmWtr = new NetworkStream(dataSocket);
        }

        private void closeDataPort()
        {
            dataStrmRdr.Close();
            dataStrmWtr.Close();
            dataSocket.Close();
            this.getSatus();

            cmdData = "ABOR" + CRLF;
            szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
            cmdSocket.Send(szData);
            this.getSatus();
        }

        private void freshFileBox_Right()
        {
            openDataPort();

            string absFilePath;

            // List
            cmdData = "LIST" + CRLF;
            szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
            cmdSocket.Send(szData);
            this.getSatus();

            Invoke((MethodInvoker)(() =>
            {
                lsb_server.Items.Clear();
            }));

            while ((absFilePath = dataStrmRdr.ReadLine()) != null)
            {
                string type;
                string prefix;
                string[] temp = Regex.Split(absFilePath, " ");
                type = temp[0].Substring(0, 1);
                switch (type)
                {
                    case "d":
                        prefix = "[目录] ";
                        break;
                    case "-":
                        prefix = "[文件] ";
                        break;
                    case "l":
                        prefix = "[连接] "; // 符号链接
                        break;
                    case "b":
                    case "c":
                        prefix = "[设备] ";
                        break;
                    default:
                        prefix = "[未知] ";
                        break;
                }

                Invoke((MethodInvoker)(() =>
                {
                    lsb_server.Items.Add(prefix + temp[temp.Length - 1]);
                }));
            }

            closeDataPort();
        }

        #endregion

        public Form1()
        {
            InitializeComponent();
            int i = int.Parse(ConfigurationManager.AppSettings["able"]);
            if (i == 0)
            {
                ConfigurationManager.AppSettings["local"] = Environment.CurrentDirectory;
                ConfigurationManager.AppSettings["able"] = "1";
            }
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
        }

        #region Button: Connect & Disconnect

        private void btn_conn_Click(object sender, EventArgs e)
        {
            if (btn_conn.Text == "连接")
            {
                Task.Run(() => ConnectToFtpServer());
            }
            else
            {
                Cursor cr = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;

                // Logout
                cmdData = "QUIT" + CRLF;
                szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                cmdSocket.Send(szData);
                this.getSatus();

                cmdSocket.Close();

                Invoke((MethodInvoker)(() =>
                {
                    lb_IP.Text = "";
                    btn_conn.Text = "连接";
                    lsb_server.Items.Clear();
                }));

                Cursor.Current = cr;
            }
        }

        private void ConnectToFtpServer()
        {
            Invoke((MethodInvoker)(() => Cursor.Current = Cursors.WaitCursor));
            try
            {
                // 解析域名
                string host = tb_IP.Text;
                string ipAddress = ResolveHostNameToIp(host);
                if (ipAddress == null)
                {
                    Invoke((MethodInvoker)(() => lsb_status.Items.Add("ERROR: 无法解析域名 " + host)));
                    return;
                }

                cmdSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                cmdSocket.Connect(ipAddress, Convert.ToInt32(tb_port.Text));
                Invoke((MethodInvoker)(() => lsb_status.Items.Clear()));

                this.getSatus();

                // Login
                cmdData = "USER " + tb_username.Text + CRLF;
                szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                cmdSocket.Send(szData);
                this.getSatus();

                cmdData = "PASS " + tb_password.Text + CRLF;
                szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                cmdSocket.Send(szData);
                string retstr = this.getSatus().Substring(0, 3);
                if (Convert.ToInt32(retstr) == 530) throw new InvalidOperationException("帐号密码错误");

                this.freshFileBox_Right();

                Invoke((MethodInvoker)(() =>
                {
                    lb_IP.Text = tb_IP.Text + ":";
                    btn_conn.Text = "断开";
                }));
            }
            catch (InvalidOperationException err)
            {
                Invoke((MethodInvoker)(() => lsb_status.Items.Add("ERROR: " + err.Message.ToString())));
            }
            finally
            {
                Invoke((MethodInvoker)(() => Cursor.Current = Cursors.Default));
            }
        }

        private string ResolveHostNameToIp(string hostName)
        {
            try
            {
                var host = Dns.GetHostEntry(hostName);
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Invoke((MethodInvoker)(() => lsb_status.Items.Add("ERROR: " + ex.Message)));
            }
            return null;
        }

        #endregion

        private void lsb_server_MouseClick(object sender, MouseEventArgs e)
        {
            int index = lsb_server.IndexFromPoint(e.X, e.Y);
            lsb_server.SelectedIndex = index;
            if (lsb_server.SelectedIndex != -1)
            {
                string selectedItem = lsb_server.SelectedItem.ToString();
                MessageBox.Show(selectedItem);

                string type = selectedItem.Substring(1, 2);
                if (type == "目录")
                {
                    cmdData = "CWD " + selectedItem.Substring(5) + CRLF;
                    lb_IP.Text += "/" + selectedItem.Substring(5);
                    szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                    cmdSocket.Send(szData);
                    string retstr = this.getSatus();
                    lsb_status.Items.Add(retstr);
                    freshFileBox_Right();
                }
                else
                {
                    MessageBox.Show("目标不是目录，无法进入！");
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string pa = lb_IP.Text;
            string retstr;
            cmdData = "CWD .." + CRLF;
            szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
            cmdSocket.Send(szData);
            retstr = this.getSatus();
            lsb_status.Items.Add(retstr);
            freshFileBox_Right();
            lb_IP.Text = pa.Substring(0, pa.LastIndexOf("/"));
        }

        private void lsb_server_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = lsb_server.IndexFromPoint(e.X, e.Y);
                lsb_server.SelectedIndex = index;
                if (lsb_server.SelectedIndex != -1)
                {
                    string selectedItem = lsb_server.SelectedItem.ToString();
                    string type = selectedItem.Substring(1, 2);
                    if (type == "文件")
                    {
                        contextMenuStrip1.Show(Control.MousePosition.X, Control.MousePosition.Y);
                    }
                }
            }
        }

        private void 下载ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Task.Run(() => DownloadFile());
        }

        private void DownloadFile()
        {
            string path = ConfigurationManager.AppSettings["local"];
            if (string.IsNullOrEmpty(path))
            {
                Invoke((MethodInvoker)(() => MessageBox.Show("请选择目标文件和下载路径", "ERROR")));
                return;
            }

            string fileName = string.Empty;
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)(() =>
                {
                    if (lsb_server.SelectedIndex < 0)
                    {
                        MessageBox.Show("请选择目标文件和下载路径", "ERROR");
                        return;
                    }
                    fileName = Regex.Split(lsb_server.Items[lsb_server.SelectedIndex].ToString(), " ")[1];
                }));
            }
            else
            {
                if (lsb_server.SelectedIndex < 0)
                {
                    MessageBox.Show("请选择目标文件和下载路径", "ERROR");
                    return;
                }
                fileName = Regex.Split(lsb_server.Items[lsb_server.SelectedIndex].ToString(), " ")[1];
            }

            string filePath = Path.Combine(path, fileName);

            long existingFileSize = 0;
            if (File.Exists(filePath))
            {
                FileInfo fileInfo = new FileInfo(filePath);
                existingFileSize = fileInfo.Length;
            }

            this.openDataPort();

            if (existingFileSize > 0)
            {
                cmdData = "REST " + existingFileSize + CRLF;
                szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                cmdSocket.Send(szData);
                this.getSatus();
            }

            cmdData = "RETR " + fileName + CRLF;
            szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
            cmdSocket.Send(szData);
            this.getSatus();

            FileStream fstrm = new FileStream(filePath, FileMode.Append);
            byte[] fbytes = new byte[1030];
            int cnt = 0;

            // 更新进度条
            long totalBytes = GetRemoteFileSize(fileName);
            Invoke((MethodInvoker)(() =>
            {
                progressBar.Maximum = 100;
                progressBar.Value = (int)((existingFileSize * 100) / totalBytes);
            }));

            while ((cnt = dataStrmWtr.Read(fbytes, 0, 1024)) > 0)
            {
                pauseEvent.WaitOne();
                fstrm.Write(fbytes, 0, cnt);

                // 更新进度条
                existingFileSize += cnt;
                Invoke((MethodInvoker)(() =>
                {
                    progressBar.Value = (int)((existingFileSize * 100) / totalBytes);
                }));
            }
            fstrm.Close();

            this.closeDataPort();
            Invoke((MethodInvoker)(() => Cursor.Current = Cursors.Default));
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }

        #region 选择路径 -void txtPath_Click(object sender, EventArgs e)

        private void txtPath_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtPath.Text = fbd.SelectedPath;
            }
        }
        #endregion

        #region 获取指定目录子目录和子文件 -void btnGetPath_Click(object sender, EventArgs e)

        private void btnGetPath_Click(object sender, EventArgs e)
        {
            tvDir.Nodes.Clear();
            if (txtPath.Text.Trim().Length == 0)
            {
                return;
            }
            else
            {
                LoadData(txtPath.Text, null);
                LoadFiles(txtPath.Text);
            }
        }
        #endregion

        #region 树控件节点点击之后触发 -void tvDir_AfterSelect(object sender, TreeViewEventArgs e)

        private void tvDir_AfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeNode node = this.tvDir.SelectedNode;
            if (node == null)
            {
                return;
            }
            string path = node.Tag.ToString();
            LoadData(path, node);
            LoadFiles(path);
        }
        #endregion

        #region 加载树节点和listview控件的项 - void LoadData(string path, TreeNode parentNode)

        void LoadData(string path, TreeNode parentNode)
        {
            if (path.Trim().Length == 0)
            {
                MessageBox.Show("路径为空");
                return;
            }
            else
            {
                DirectoryInfo dir = new DirectoryInfo(path);
                DirectoryInfo[] dirs = dir.GetDirectories();
                foreach (DirectoryInfo d in dirs)
                {
                    TreeNode subNode = new TreeNode(d.Name);
                    subNode.Tag = d.FullName;
                    if (parentNode == null)
                    {
                        tvDir.Nodes.Add(subNode);
                    }
                    else
                    {
                        parentNode.Nodes.Add(subNode);
                    }
                    LoadData(subNode.Tag.ToString(), subNode);
                }
            }
        }
        #endregion

        #region 加载文件 ListView- void LoadFiles(string path)

        void LoadFiles(string path)
        {
            DirectoryInfo dir = new DirectoryInfo(path);
            this.tvDir.ExpandAll();
            this.lvList.Items.Clear();
            FileInfo[] myFiles = dir.GetFiles();
            foreach (FileInfo f in myFiles)
            {
                ListViewItem lv = new ListViewItem(f.Name);
                lv.SubItems.AddRange(new string[] { f.Extension, (f.Length / 1024).ToString() + " KB", f.FullName });
                lv.Tag = f.FullName;
                this.lvList.Items.Add(lv);
            }
        }
        #endregion

        private void 上传ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Task.Run(() => UploadFile());
        }

        private void UploadFile()
        {
            Invoke((MethodInvoker)(() => Cursor.Current = Cursors.WaitCursor));

            if (btn_conn.Text == "断开")
            {
                string fileName = string.Empty;
                string filePath = string.Empty;
                Invoke((MethodInvoker)(() =>
                {
                    fileName = lvList.SelectedItems[0].Text;
                    filePath = lvList.SelectedItems[0].SubItems[3].Text;
                }));

                this.openDataPort();

                long existingFileSize = GetRemoteFileSize(fileName);
                if (existingFileSize > 0)
                {
                    cmdData = "REST " + existingFileSize + CRLF;
                    szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                    cmdSocket.Send(szData);
                    this.getSatus();
                }

                cmdData = "STOR " + fileName + CRLF;
                szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                cmdSocket.Send(szData);
                this.getSatus();

                FileStream fstrm = new FileStream(filePath, FileMode.Open);
                fstrm.Seek(existingFileSize, SeekOrigin.Begin);
                byte[] fbytes = new byte[1030];
                int cnt = 0;
                while ((cnt = fstrm.Read(fbytes, 0, 1024)) > 0)
                {
                    pauseEvent.WaitOne();
                    dataStrmWtr.Write(fbytes, 0, cnt);
                }
                fstrm.Close();

                this.closeDataPort();
                this.freshFileBox_Right();
            }
            else
            {
                Invoke((MethodInvoker)(() => MessageBox.Show("请先连接！")));
            }
            Invoke((MethodInvoker)(() => Cursor.Current = Cursors.Default));
        }

        private long GetRemoteFileSize(string fileName)
        {
            cmdData = "SIZE " + fileName + CRLF;
            szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
            cmdSocket.Send(szData);
            string retstr = this.getSatus();
            if (retstr.StartsWith("213"))
            {
                return long.Parse(retstr.Substring(4));
            }
            return 0;
        }

        private void lvList_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && lvList.SelectedIndices.Count != 0)
            {
                contextMenuStrip2.Show(Control.MousePosition.X, Control.MousePosition.Y);
            }
        }

        private void btnPauseResume_Click(object sender, EventArgs e)
        {
            if (isPaused)
            {
                pauseEvent.Set();
                btnPauseResume.Text = "暂停";
                isPaused = false;
            }
            else
            {
                pauseEvent.Reset();
                btnPauseResume.Text = "继续";
                isPaused = true;
            }
        }
    }
}
