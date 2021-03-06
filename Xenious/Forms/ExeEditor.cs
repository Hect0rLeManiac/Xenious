﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xbox360;
using Xbox360.Kernal.Memory;
using Xbox360.XEX;

namespace Xenious.Forms
{
    public partial class ExeEditor : Form
    {
        /* Version for Launcher. */
        public static string tool_version = "0.0.600.0";

        /* Needed Pointers */
        Xbox360.Kernal.Memory.XboxMemory in_mem;
        public List<Database.PEFileDatabase> pe_dbs;
        bool has_xextool = false;

        /* Editor components. */
        RichTextBox richTextBox2;
        
        public void __log(string msg)
        {
            richTextBox1.Text += string.Format("[ {0} ] - {1}\n", DateTime.Now.ToString(), msg);
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.ScrollToCaret();
        }
        public void __cmd(string[] cmd)
        {
            switch (cmd[0])
            {
                case "goto":
                    switch (cmd[1])
                    {
                        case "func":
                            #region Load Function
                            UInt32 pos = UInt32.Parse(cmd[2]);

                            #region Get PEFileDatabse
                            Database.PEFileDatabase pefd = new Database.PEFileDatabase();
                            foreach(Database.PEFileDatabase pfd in pe_dbs)
                            {
                                if(pos < pfd.end_address && pos >= pfd.start_address)
                                {
                                    pefd = pfd;
                                }
                            }
                            #endregion
                            #region Get FileSection
                            Database.PEFileSection pefs = new Database.PEFileSection();
                            // Find the section.
                            foreach(Database.PEFileSection sec in pefd.sections)
                            {
                                if(cmd[3] == sec.section_name)
                                {
                                    pefs = sec;
                                }
                            }
                            #endregion

                            // Now loop through and find function.
                            Database.PEFunction pef = new Database.PEFunction();
                            foreach(Database.PEFunction pf in pefs.functions)
                            {
                                if(cmd[4] == pf.func_name)
                                {
                                    pef = pf;
                                }
                            }

                            // Add RichtextBox Editor to panel.
                            richTextBox2 = new RichTextBox();
                            richTextBox2.Dock = DockStyle.Fill;

                            splitContainer1.Panel2.Controls.Clear();
                            splitContainer1.Panel2.Controls.Add(richTextBox2);

                            // Clear other.
                            richTextBox2.Text = "";

                            // Starting Address.
                            UInt32 start_addr = pef.start_address;

                            richTextBox2.Text += string.Format("{0}:\n\n", pef.func_name);
                            foreach(byte[] op in pef.op_codes)
                            {
                                // Check for Endianness.
                                if(BitConverter.IsLittleEndian)
                                {
                                    Array.Reverse(op);
                                }

                                richTextBox2.Text += string.Format("0x{0}        {1}\n", start_addr.ToString("X8"), XenonPowerPC.PowerPC.Functions.find_func(BitConverter.ToUInt32(op, 0)).op);
                                start_addr += 4;
                            }
                            #endregion
                            break;
                    }
                    break;
                case "open":
                    switch(cmd[1])
                    {
                        case "hexeditor":
                            // Get Section of Hex.
                            UInt32 addr = UInt32.Parse(cmd[2]);
                            int size = int.Parse(cmd[3]);

                            // Get Data.
                            in_mem.Position = addr;
                            byte[] data = in_mem.ReadBytes(size, false);

                            // Load Byte Viewer
                            ByteViewer section_panel = new ByteViewer();
                            section_panel.Dock = DockStyle.Fill;
                            section_panel.SetBytes(data);
                            splitContainer1.Panel2.Controls.Clear();
                            splitContainer1.Panel2.Controls.Add(section_panel);
                            break;
                    }
                    break;
            }
        }

        /* Init Funcs */
        public void clear_cache()
        {
            string[] files = Directory.GetFiles(Application.StartupPath + "/cache");

            foreach (string file in files)
            {
                File.Delete(file);
            }
            __log("Cache has been cleared...");
        }

        /* XEX Loading Functions */
        public void show_loader(XenonExecutable in_xex)
        {
            Forms.Dialogs.XboxMemoryLoader loader = new Dialogs.XboxMemoryLoader(in_xex);
            loader.ShowDialog();

            // Check if we need kernal memory aswell.
            bool kernal = false;
            if (loader.kernal_imports.Count > 0)
            {
                foreach (Xecutable.KernalImport imp in loader.kernal_imports)
                {
                    if (imp.include == true)
                    {
                        kernal = true;
                    }
                }
            }
            if (kernal) // Just game memory needed.
            {
                in_mem = new Xbox360.Kernal.Memory.XboxMemory(0x20000000);
            }
            else { 
                in_mem = new Xbox360.Kernal.Memory.XboxMemory(0x10000000);
            }

            // Setup Xenon Memory.
            Xecutable.XenonMemory.setup_xenon_memory(in_xex, loader.local_imports, loader.kernal_imports, in_mem);
        }
        public void close_xex()
        {
            // Close IO Pointers.
            if (in_mem != null)
            {
                in_mem.close();
            }

            // Delete Memory.
            in_mem = null;
            if (File.Exists(Application.StartupPath + "cache/xbox_memory.bin"))
            {
                File.Delete(Application.StartupPath + "cache/xbox_memory.bin");
            }
            disable_gui();
            __log("Closing Xenon Memory...");
        }
        public void init_destroyer(XenonExecutable in_xex)
        {
            // Show progress dialog.
            Forms.Dialogs.LoadProgressDialog lpd = new Dialogs.LoadProgressDialog();

            // Init progress dialog.
            lpd.set_progress(0);
            lpd.set_status("Xenon Executable loaded...");
            lpd.Show();

            // Setup PEDatabase.

            // First check if we have a local one in the database.
            
            pe_dbs = new List<Database.PEFileDatabase>();
            Database.PEFileDatabase pe_db = Xecutable.Database.generate_pe_file_template(in_xex);

            // Start to destroy.
            lpd.set_status("Decompiling MainApp Xenon Executable...");
            if (Xecutable.XEXLoader.load_mainapp_from_load_address(pe_db, in_mem) != true)
            {
                throw new Exception("Wtf :(");
            }
            lpd.set_status("Decompiled MainApp Xenon Executable :)");
            lpd.set_progress(30);

            // Add to Database.
            pe_dbs.Add(pe_db);

            // Now this may take a while, destroy imports.
            lpd.set_status("Decompiling imports...");

            lpd.set_status("Constructing GUI...");
            
            // Now Init GUI.
            init_gui();

            // Enable GUI.
            enable_gui();
            lpd.set_progress(100);
            lpd.set_status("All Done :)");
            lpd.Close();
        }
        public XenonExecutable load_xex(string filename)
        {
            XenonExecutable in_xex;
            // Load XEX.
            try
            {
                in_xex = new XenonExecutable(filename);
                in_xex.read_header();
                in_xex.parse_certificate();
                in_xex.parse_sections();

                int x = in_xex.parse_optional_headers();

                if (x > 0)
                {
                    throw new Exception("Unable to parse Xenon Executable Option Headers, ErrorCode : " + x.ToString());
                }

                // Check for encryption.
                if (in_xex.base_file_info_h.enc_type == XeEncryptionType.Encrypted ||
                    in_xex.base_file_info_h.comp_type == XeCompressionType.Compressed ||
                    in_xex.base_file_info_h.comp_type == XeCompressionType.DeltaCompressed)
                {
                    if (has_xextool == false)
                    {
                        MessageBox.Show("Either add xextool to the local directory in a bin folder or decrypt and decompress this executable...", "Error : ");
                        return null;
                    }
                    else
                    {
                        // Copy over any imports.
                        #region Copy over Imports to cache dir.
                        foreach (XeImportLibary lib in in_xex.import_libs)
                        {
                            if(System.IO.File.Exists(System.IO.Path.GetDirectoryName(in_xex.IO.file) + "/" + lib.name))
                            {
                                __log("Copying over " + lib.name + " to local cache...");
                                System.IO.File.Copy(System.IO.Path.GetDirectoryName(in_xex.IO.file) + "/" + lib.name, Application.StartupPath + "/cache/" + lib.name);
                            }
                        }
                        #endregion

                        #region Decrypt and Decompress XEX
                        // Write over original xex.
                        if (Xecutable.xextool.xextool_to_raw_xextool(in_xex.IO.file, Application.StartupPath + "/cache/original.xex"))
                        {
                            // Parse all xex meta info.
                            try
                            {
                                close_xex();

                                // Open New xex.
                                in_xex = new XenonExecutable(Application.StartupPath + "/cache/original.xex");

                                // Parse xex.
                                in_xex.read_header();
                                in_xex.parse_certificate();
                                in_xex.parse_sections();
                                x = in_xex.parse_optional_headers();
                                if (x != 0)
                                {
                                    throw new Exception("Unable to parse optional headers, error code : " + x.ToString());
                                }
                            }
                            catch
                            {
                                throw new Exception("Unable to parse the xenon executable meta...");
                            }
                        }
                        else
                        {
                            __log("Unable to open decompressed and decrypted cache file...");
                            in_xex = null;
                            return null;
                        }
                        #endregion
                    }
                }
            }
            catch
            {
                throw new Exception("Unable to parse Xenon Executable...");
            }

            // Now check we have a pe.
            if(!in_xex.load_pe())
            {
                throw new Exception("Error : Expecting a PE File...");
            }

            // Init Xbox memory, Show Loader.
            show_loader(in_xex);
            return in_xex;
        }

        /* GUI Functions. */
        public void enable_gui()
        {
            treeView1.Enabled = true;
            treeView1.Update();
            saveToolStripMenuItem.Enabled = false;
            closeToolStripMenuItem.Enabled = true;
            splitContainer1.Panel2.Controls.Clear();
        }
        public void disable_gui()
        {
            treeView1.Nodes.Clear();
            treeView1.Enabled = false;
            treeView1.Update();
            saveToolStripMenuItem.Enabled = false;
            closeToolStripMenuItem.Enabled = false;
            splitContainer1.Panel2.Controls.Clear();
        }
        public void init_gui()
        {
            // First node is main executable.
            TreeNode main = new TreeNode();
            main.Text = (in_mem.MainApp.has_header_key(XeHeaderKeys.ORIGINAL_PE_NAME) ? in_mem.MainApp.orig_pe_name : Path.GetFileName(in_mem.MainApp.IO.file));

            // Add Sections and Functions.
            for(int i = 0; i < pe_dbs[0].sections.Count; i++)
            {
                TreeNode sn = new TreeNode();
                sn.Text = pe_dbs[0].sections[i].section_name;

                // Add Imports.
                if (pe_dbs[0].sections[i].imports != null && pe_dbs[0].sections[i].imports.Count > 0)
                {
                    TreeNode imps = new TreeNode();
                    imps.Text = "Imports";
                    imps.Tag = "goto imports " + pe_dbs[0].sections[i].section_name;
                    sn.Nodes.Add(imps);
                }

                // Add Functions.
                for(int x = 0; x < pe_dbs[0].sections[i].functions.Count; x++)
                {
                    TreeNode fnc = new TreeNode();
                    fnc.Text = pe_dbs[0].sections[i].functions[x].func_name;
                    fnc.Tag = string.Format("goto func {0} {1} {2}", 
                        pe_dbs[0].sections[i].functions[x].start_address.ToString(), 
                        pe_dbs[0].sections[i].section_name,
                        pe_dbs[0].sections[i].functions[x].func_name);
                    sn.Nodes.Add(fnc);
                }

                main.Nodes.Add(sn);
            }

            if(in_mem.MainApp.has_header_key(XeHeaderKeys.RESOURCE_INFO)) {
                // Add resources.
                TreeNode ress = new TreeNode();
                ress.Text = "Resources";

                foreach (XeResourceInfo res in in_mem.MainApp.resources)
                {
                    TreeNode resn = new TreeNode();
                    resn.Text = res.name.Replace("\n", "").Replace("\0", "").Replace("\r", "");
                    resn.Tag = "open hexeditor " + res.address + " " + res.size;
                    ress.Nodes.Add(resn);
                }

                main.Nodes.Add(ress);
            }
            treeView1.Nodes.Add(main);

            // Now Add Imports.
            List<XenonExecutable> imports = in_mem.AppImports;

            if(imports != null && imports.Count > 0)
            {
                for (int i = 0; i < imports.Count; i++)
                {
                    TreeNode import = new TreeNode();
                    import.Text = (imports[i].has_header_key(XeHeaderKeys.ORIGINAL_PE_NAME) ? imports[i].orig_pe_name : Path.GetFileName(imports[i].IO.file));


                    treeView1.Nodes.Add(import);
                }
            }
            
        }

        public ExeEditor()
        {
            InitializeComponent();
        }
        private void XEXDebugger_Load(object sender, EventArgs e)
        {
            __log("Xenon Executable Editor and Debugger has started...");

            // Check for xextool Support.
            if(Xecutable.xextool.check_xextool_exists())
            {
                has_xextool = true;
                __log("xextool Tool Support Enabled...");
            }
            else
            {
                MessageBox.Show("This app needs xextool, please install xextool...", "Error : ");
                this.Close();
            }

            // Check for cache dir.
            if(Directory.Exists(Application.StartupPath + "/cache/") == false)
            {
                Directory.CreateDirectory(Application.StartupPath + "/cache/");
            }

            clear_cache();
            disable_gui();
        }
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Xenon Executables (*.xex*)|*.xex|All Files (*.*)|*.";
            ofd.Title = "Please open a Xenon Executable...";

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                close_xex(); // Close anyway.
                XenonExecutable xex = load_xex(ofd.FileName); // Load xex.
                init_destroyer(xex);
            }
        }
        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            if(treeView1.SelectedNode != null && treeView1.SelectedNode.Tag != null)
            {
                string[] cmd = treeView1.SelectedNode.Tag.ToString().Split(' ');

                __cmd(cmd);
            }
        }
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(this.in_mem != null)
            {
                this.close_xex();
            }
            this.Close();
        }
        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            close_xex();

        }
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {

        }

        private void XEXDebugger_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.in_mem != null)
            {
                this.close_xex();
            }
        }
    }
}
