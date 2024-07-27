using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Configuration;
using System.Drawing;

namespace WindowsFormsApp5
{
    public partial class Form1 : Form
    {
        #region  Private variable
        private Socket cmdSocket;
        private Socket dataSocket;
        private NetworkStream cmdStrmWtr;
        private StreamReader cmdStrmRdr;
        private NetworkStream dataStrmWtr;
        private StreamReader dataStrmRdr;
        private String cmdData;
        private byte[] szData;
        private const String CRLF = "\r\n";
        #endregion
        private Image folderIcon;
        private Image fileIcon;
        #region  Private Functions

        private String getSatus()
        {
            byte[] buffer = new byte[1024];
            int bytesRead = cmdSocket.Receive(buffer);
            String ret = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            lsb_status.Items.Add(ret);
            lsb_status.SelectedIndex = lsb_status.Items.Count - 1;
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
            lsb_status.Items.Add("Get dataPort=" + dataPort);

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

            lsb_server.Items.Clear();
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

                // 创建 ListItem 对象
                ListItem item = new ListItem
                {
                    Text = temp[temp.Length - 1],
                    IsFolder = type == "d",
                    OriginalData = prefix + temp[temp.Length - 1]
                };

                // 将 ListItem 对象添加到 ListBox
                lsb_server.Items.Add(item);
            }

            closeDataPort();
        }


        #endregion

        public Form1()
        {
            InitializeComponent();

            // 加载图标
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Directory.GetParent(basePath).Parent.Parent.FullName;
            string folderIconPath = Path.Combine(projectRoot, "source", "pic1.png");
            string fileIconPath = Path.Combine(projectRoot, "source", "pic2.png");

            folderIcon = new Bitmap(Image.FromFile(folderIconPath), new Size(24, 24));
            fileIcon = new Bitmap(Image.FromFile(fileIconPath), new Size(24, 24));


            // 设置 ListBox 为 OwnerDraw 模式
            lsb_server.DrawMode = DrawMode.OwnerDrawFixed;
            lsb_server.DrawItem += new DrawItemEventHandler(lsb_server_DrawItem);

            int i = int.Parse(ConfigurationManager.AppSettings["able"]);
            if (i == 0)
            {
                // 默认
                ConfigurationManager.AppSettings["local"] = Environment.CurrentDirectory;
                ConfigurationManager.AppSettings["able"] = "1";
            }
        }
        private void lsb_server_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();

            if (lsb_server.Items[e.Index] is ListItem item)
            {
                Image icon = item.IsFolder ? folderIcon : fileIcon;
                e.Graphics.DrawImage(icon, e.Bounds.Left, e.Bounds.Top);
                e.Graphics.DrawString(item.OriginalData, e.Font, Brushes.Black, e.Bounds.Left + icon.Width, e.Bounds.Top);
            }

            e.DrawFocusRectangle();
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
        }

        #region  Button:  Connect & Disconnect

        private void btn_conn_Click(object sender, EventArgs e)
        {
            if (btn_conn.Text == "连接")
            {
                Cursor cr = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    // 解析域名
                    string host = tb_IP.Text;
                    string ipAddress = ResolveHostNameToIp(host);
                    if (ipAddress == null)
                    {
                        lsb_status.Items.Add("ERROR: 无法解析域名 " + host);
                        return;
                    }

                    cmdSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    cmdSocket.Connect(ipAddress, Convert.ToInt32(tb_port.Text));
                    lsb_status.Items.Clear();

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

                    lb_IP.Text = tb_IP.Text + ":";
                    btn_conn.Text = "断开";
                }
                catch (InvalidOperationException err)
                {
                    lsb_status.Items.Add("ERROR: " + err.Message.ToString());
                }
                finally
                {
                    Cursor.Current = cr;
                }
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

                lb_IP.Text = "";
                btn_conn.Text = "连接";
                lsb_server.Items.Clear();

                Cursor.Current = cr;
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
                lsb_status.Items.Add("ERROR: " + ex.Message);
            }
            return null;
        }

        #endregion

        private void lsb_server_MouseClick(object sender, MouseEventArgs e)
        {
            string retstr;
            string type;
            int index = lsb_server.IndexFromPoint(e.X, e.Y);
            lsb_server.SelectedIndex = index;
            if (lsb_server.SelectedIndex != -1)
            {
                MessageBox.Show(lsb_server.SelectedItem.ToString());

                type = lsb_server.SelectedItem.ToString().Substring(1, 2);
                if (type == "目录")
                {
                    cmdData = "CWD " + lsb_server.SelectedItem.ToString().Substring(5) + CRLF;
                    lb_IP.Text += "/" + lsb_server.SelectedItem.ToString().Substring(5);
                    szData = Encoding.ASCII.GetBytes(cmdData.ToCharArray());
                    cmdSocket.Send(szData);
                    retstr = this.getSatus();
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
                if (index != ListBox.NoMatches)
                {
                    lsb_server.SelectedIndex = index;
                    if (lsb_server.SelectedItem is ListItem selectedItem)
                    {
                        // 使用 selectedItem.OriginalData 或 selectedItem.Text 进行后续操作
                        string abc = selectedItem.OriginalData;
                        string type = abc.Substring(1, 2);
                        if (type == "文件")
                        {
                            contextMenuStrip1.Show(Control.MousePosition.X, Control.MousePosition.Y);
                        }
                    }
                }
            }
        }


        private void 下载ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string path = ConfigurationManager.AppSettings["local"];
            if (string.IsNullOrEmpty(path) || lsb_server.SelectedIndex < 0)
            {
                MessageBox.Show("请选择目标文件和下载路径", "ERROR");
                return;
            }

            Cursor cr = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;

            if (lsb_server.SelectedItem is ListItem selectedItem)
            {
                string fileName = selectedItem.Text;
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

                using (FileStream fstrm = new FileStream(filePath, FileMode.Append))
                {
                    byte[] fbytes = new byte[1030];
                    int cnt = 0;
                    while ((cnt = dataStrmWtr.Read(fbytes, 0, 1024)) > 0)
                    {
                        fstrm.Write(fbytes, 0, cnt);
                    }
                }

                this.closeDataPort();
            }

            Cursor.Current = cr;
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
            Cursor cr = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;

            if (btn_conn.Text == "断开")
            {
                string fileName = lvList.SelectedItems[0].Text;
                string filePath = lvList.SelectedItems[0].SubItems[3].Text;

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
                    dataStrmWtr.Write(fbytes, 0, cnt);
                }
                fstrm.Close();

                this.closeDataPort();
                this.freshFileBox_Right();
            }
            else
            {
                MessageBox.Show("请先连接！");
            }
            Cursor.Current = cr;
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

        private void lsb_server_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
    public class ListItem
    {
        public string Text { get; set; }
        public bool IsFolder { get; set; }
        public string OriginalData { get; set; }
    }

}