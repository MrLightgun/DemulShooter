﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace DemulShooter
{
    class Game_RwSDR : Game
    {
        private const string FOLDER_GAMEDATA = @"MemoryData\ringwide";

        /*** MEMORY ADDRESSES **/
        protected int _Controls_Base_Address;
        protected int _P1_X_Offset;
        protected int _P1_Y_Offset;
        protected int _P1_Buttons_Offset;
        protected int _P2_X_Offset;
        protected int _P2_Y_Offset;
        protected int _P2_Buttons_Offset;
        protected string _Axis_NOP_Offset;
        protected int _Buttons_Injection_Offset;
        protected int _Buttons_Injection_Return_Offset;

        private bool _ParrotLoaderFullHack = false;
        private const byte _ParrotLoader_P1_Start_ScanCode = 0x02;  //[1]
        private const byte _ParrotLoader_P2_Start_ScanCode = 0x03;  //[2]
        private const byte _ParrotLoader_Service_ScanCode = 0x09;   //[8]
        private const byte _ParrotLoader_Test_ScanCode = 0x0A;      //[9]

        /// <summary>
        /// Constructor
        /// </summary>
        public Game_RwSDR(string RomName, bool ParrotLoaderFullHack, bool Verbose)
            : base()
        {
            GetScreenResolution();

            _RomName = RomName;
            _ParrotLoaderFullHack = ParrotLoaderFullHack;
            _VerboseEnable = Verbose;
            _ProcessHooked = false;
            _Target_Process_Name = "game";

            ReadGameData();
            _tProcess = new Timer();
            _tProcess.Interval = 500;
            _tProcess.Tick += new EventHandler(tProcess_Tick);
            _tProcess.Enabled = true;
            _tProcess.Start();

            WriteLog("Waiting for RingWide " + _RomName + " game to hook.....");
        }

        /// <summary>
        /// Timer event when looking for Process (auto-Hook and auto-close)
        /// </summary>
        private void tProcess_Tick(Object Sender, EventArgs e)
        {
            if (!_ProcessHooked)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(_Target_Process_Name);
                    if (processes.Length > 0)
                    {
                        _TargetProcess = processes[0];
                        _ProcessHandle = _TargetProcess.Handle;
                        _TargetProcess_MemoryBaseAddress = _TargetProcess.MainModule.BaseAddress;

                        if (_TargetProcess_MemoryBaseAddress != IntPtr.Zero)
                        {
                            byte[] bTampon = ReadBytes((int)_TargetProcess_MemoryBaseAddress + 0x0308A0C4, 4);
                            _Controls_Base_Address = bTampon[0] + bTampon[1] * 256 + bTampon[2] * 65536 + bTampon[3] * 16777216;
                            if (_Controls_Base_Address != 0)
                            {
                                _ProcessHooked = true;
                                WriteLog("Attached to Process " + _Target_Process_Name + ".exe, ProcessHandle = " + _ProcessHandle);
                                WriteLog(_Target_Process_Name + ".exe = 0x" + _TargetProcess_MemoryBaseAddress.ToString("X8"));
                                WriteLog("Controls base address = 0x" + _Controls_Base_Address.ToString("X8"));
                                if (_ParrotLoaderFullHack)
                                    SetHack2();
                                else
                                    SetHack();
                            }   
                        }
                    }
                }
                catch
                {
                    WriteLog("Error trying to hook " + _Target_Process_Name + ".exe");
                }
            }
            else
            {
                Process[] processes = Process.GetProcessesByName(_Target_Process_Name);
                if (processes.Length <= 0)
                {
                    _ProcessHooked = false;
                    _TargetProcess = null;
                    _ProcessHandle = IntPtr.Zero;
                    _TargetProcess_MemoryBaseAddress = IntPtr.Zero;
                    WriteLog(_Target_Process_Name + ".exe closed");
                    Environment.Exit(0);
                }
            }
        }


        #region Screen

        public override bool GameScale(MouseInfo Mouse, int Player)
        {
            if (_ProcessHandle != IntPtr.Zero)
            {
                try
                {
                    Win32.Rect TotalRes = new Win32.Rect();
                    Win32.GetClientRect(_TargetProcess.MainWindowHandle, ref TotalRes);
                    double TotalResX = TotalRes.Right - TotalRes.Left;
                    double TotalResY = TotalRes.Bottom - TotalRes.Top;

                    WriteLog("Game client window resolution (Px) = [ " + TotalResX + "x" + TotalResY + " ]");

                    //X => [0-E1] = 225
                    //Y => [0-E1] = 225
                    //Axes inversés : 0 = Bas et Droite
                    double dMaxX = 225.0;
                    double dMaxY = 225.0;

                    Mouse.pTarget.X = Convert.ToInt32(dMaxX - Math.Round(dMaxX * Mouse.pTarget.X / TotalResX));
                    Mouse.pTarget.Y = Convert.ToInt32(dMaxY - Math.Round(dMaxY * Mouse.pTarget.Y / TotalResY));
                    if (Mouse.pTarget.X < 0)
                        Mouse.pTarget.X = 0;
                    if (Mouse.pTarget.Y < 0)
                        Mouse.pTarget.Y = 0;
                    if (Mouse.pTarget.X > (int)dMaxX)
                        Mouse.pTarget.X = (int)dMaxX;
                    if (Mouse.pTarget.Y > (int)dMaxY)
                        Mouse.pTarget.Y = (int)dMaxY;
                    return true;
                }
                catch (Exception ex)
                {
                    WriteLog("Error scaling mouse coordonates to GameFormat : " + ex.Message.ToString());
                }
            }
            return false;
        }

        #endregion

        #region File I/O

        /// <summary>
        /// Read memory values in .cfg file
        /// </summary>
        protected override void ReadGameData()
        {
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + @"\" + FOLDER_GAMEDATA + @"\" + _RomName + ".cfg"))
            {
                using (StreamReader sr = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + @"\" + FOLDER_GAMEDATA + @"\" + _RomName + ".cfg"))
                {
                    string line;
                    line = sr.ReadLine();
                    while (line != null)
                    {
                        string[] buffer = line.Split('=');
                        if (buffer.Length > 1)
                        {
                            try
                            {
                                switch (buffer[0].ToUpper().Trim())
                                {
                                    case "P1_X_OFFSET":
                                        _P1_X_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "P1_Y_OFFSET":
                                        _P1_Y_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "P1_BUTTONS_OFFSET":
                                        _P1_Buttons_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "P2_X_OFFSET":
                                        _P2_X_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "P2_Y_OFFSET":
                                        _P2_Y_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "P2_BUTTONS_OFFSET":
                                        _P2_Buttons_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "AXIS_NOP_OFFSET":
                                        _Axis_NOP_Offset = buffer[1].Trim();
                                        break;
                                    case "BUTTONS_INJECTION_OFFSET":
                                        _Buttons_Injection_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    case "BUTTONS_INJECTION_RETURN_OFFSET":
                                        _Buttons_Injection_Return_Offset = int.Parse(buffer[1].Substring(3).Trim(), NumberStyles.HexNumber);
                                        break;
                                    default: break;
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteLog("Error reading game data : " + ex.Message.ToString());
                            }
                        }
                        line = sr.ReadLine();
                    }
                    sr.Close();
                }
            }
            else
            {
                WriteLog("File not found : " + AppDomain.CurrentDomain.BaseDirectory + @"\" + FOLDER_GAMEDATA + @"\" + _RomName + ".cfg");
            }
        }

        #endregion

        #region MemoryHack

        /// <summary>
        /// Genuine Hack, just blocking Axis and Triggers input to replace them
        /// Reverse back to it when DumbJVSCommand will be working with ParrotLoader, without DumbJVSManager
        /// </summary>
        private void SetHack()
        {
            //NOPing axis proc
            SetNops((int)_TargetProcess_MemoryBaseAddress, _Axis_NOP_Offset);

            //Hacking buttons proc : 
            //Same byte is used for both triggers, start and service (for each player)
            //0b10000000 is Start
            //0b01000000 is Service
            //0b00000010 is Trigger
            //So we need to make a mask to accept Start button moodification and block other so we can inject
            Memory CaveMemory = new Memory(_TargetProcess, _TargetProcess.MainModule.BaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);
            List<Byte> Buffer = new List<Byte>();
            //push esi
            CaveMemory.Write_StrBytes("56");
            //and esi,00000080
            CaveMemory.Write_StrBytes("81 E6 80 00 00 00");
            //cmp esi,00
            CaveMemory.Write_StrBytes("83 FE 00");
            //jg @ => if Start PRessed
            CaveMemory.Write_StrBytes("0F 8F 0D 00 00 00");
            //and dword ptr [ebp+ecx*2+08],7F => Putting the start bit to 0
            CaveMemory.Write_StrBytes("81 64 4D 08 7F FF FF FF");
            //jmp @
            CaveMemory.Write_StrBytes("E9 08 00 00 00");
            //or [ebp+ecx*2+08],00000080 ==> start is pressed, putting bit to 1
            CaveMemory.Write_StrBytes("81 4C 4D 08 80 00 00 00");
            //pop esi
            CaveMemory.Write_StrBytes("5E");
            //and esi,00000040
            CaveMemory.Write_StrBytes("83 E6 40");
            //cmp esi,00
            CaveMemory.Write_StrBytes("83 FE 00");
            //jg @ => if Service PRessed
            CaveMemory.Write_StrBytes("0F 8F 0A 00 00 00");
            //and [ebp+ecx*2+08],000000BF => Putting the Service bit to 0
            CaveMemory.Write_StrBytes("83 64 4D 08 BF");
            //jmp @
            CaveMemory.Write_StrBytes("E9 05 00 00 00");
            //or dword ptr [ebp+ecx*2+08],40 ==> Service is pressed, putting bit to 1
            CaveMemory.Write_StrBytes("83 4C 4D 08 40");
            //Jump back
            CaveMemory.Write_jmp((int)_TargetProcess.MainModule.BaseAddress + _Buttons_Injection_Return_Offset);

            WriteLog("Adding CodeCave at : 0x" + CaveMemory.CaveAddress.ToString("X8"));

            //Injection de code
            IntPtr ProcessHandle = _TargetProcess.Handle;
            int bytesWritten = 0;
            int jumpTo = 0;
            jumpTo = CaveMemory.CaveAddress - ((int)_TargetProcess.MainModule.BaseAddress + _Buttons_Injection_Offset) - 5;
            Buffer = new List<byte>();
            Buffer.Add(0xE9);
            Buffer.AddRange(BitConverter.GetBytes(jumpTo));            
            Win32.WriteProcessMemory((int)ProcessHandle, (int)_TargetProcess.MainModule.BaseAddress + _Buttons_Injection_Offset, Buffer.ToArray(), Buffer.Count, ref bytesWritten);

            WriteLog("Memory Hack complete !");
            WriteLog("-");
        }

        /// <summary>
        /// Temporary hack NOPing all inputs while DumbJVSCommand is not effective
        /// </summary>
        private void SetHack2()
        {
            //NOPing axis proc
            SetNops((int)_TargetProcess_MemoryBaseAddress, _Axis_NOP_Offset);
            //NOPing TEST proc
            SetNops((int)_TargetProcess_MemoryBaseAddress, "0x000DB396|3");
            SetNops((int)_TargetProcess_MemoryBaseAddress, "0x000DB1DA|3");
            //NOPing Buttons proc
            SetNops((int)_TargetProcess_MemoryBaseAddress, "0x000DB3EC|5");

            //Enabling Keyboard listening
            ApplyKeyboardHook();

            WriteLog("Memory Hack complete !");
            WriteLog("-");
        }

        // Keyboard only used with -parrotloader switch to overwrite all parrot controls
        protected override IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Win32.KBDLLHOOKSTRUCT s = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));                   
                if ((UInt32)wParam == Win32.WM_KEYDOWN)
                {
                    switch (s.scanCode)
                    {
                        case _ParrotLoader_P1_Start_ScanCode:
                            {
                                Apply_OR_ByteMask(_Controls_Base_Address + _P1_Buttons_Offset, 0x80);
                            }break;
                        case _ParrotLoader_P2_Start_ScanCode:
                            {
                                Apply_OR_ByteMask(_Controls_Base_Address + _P2_Buttons_Offset, 0x80);
                            } break;
                        case _ParrotLoader_Service_ScanCode:
                            {
                                Apply_OR_ByteMask(_Controls_Base_Address + _P1_Buttons_Offset, 0x40);
                            } break;
                        case _ParrotLoader_Test_ScanCode:
                            {
                                WriteByte(_Controls_Base_Address + 0x04, 0x80);
                            }break;
                        default:
                            break;
                    }
                }
                else if ((UInt32)wParam == Win32.WM_KEYUP)
                {
                    switch (s.scanCode)
                    {
                        case _ParrotLoader_P1_Start_ScanCode:
                            {
                                Apply_AND_ByteMask(_Controls_Base_Address + _P1_Buttons_Offset, 0x7F);
                            } break;
                        case _ParrotLoader_P2_Start_ScanCode:
                            {
                                Apply_AND_ByteMask(_Controls_Base_Address + _P2_Buttons_Offset, 0x7F);
                            } break;
                        case _ParrotLoader_Service_ScanCode:
                            {
                                Apply_AND_ByteMask(_Controls_Base_Address + _P1_Buttons_Offset, 0xBF);
                            } break;
                        case _ParrotLoader_Test_ScanCode:
                            {
                                WriteByte(_Controls_Base_Address + 0x04, 0x00);
                            } break;
                        default:
                            break;
                    }
                }
            }            
            return Win32.CallNextHookEx(_KeyboardHookID, nCode, wParam, lParam);
        }       

        public override void SendInput(MouseInfo mouse, int Player)
        {
            byte[] bufferX = { (byte)(mouse.pTarget.X & 0xFF), (byte)(mouse.pTarget.X >> 8) };
            byte[] bufferY = { (byte)(mouse.pTarget.Y & 0xFF), (byte)(mouse.pTarget.Y >> 8) };

            if (Player == 1)
            {
                //Write Axis
                WriteByte(_Controls_Base_Address + _P1_X_Offset, bufferX[0]);
                WriteByte(_Controls_Base_Address + _P1_Y_Offset, bufferY[0]);

                //Inputs
                if (mouse.button == Win32.RI_MOUSE_LEFT_BUTTON_DOWN || mouse.button == Win32.RI_MOUSE_MIDDLE_BUTTON_DOWN)
                {
                    byte b = ReadByte(_Controls_Base_Address + _P1_Buttons_Offset);
                    b |= 0x02;
                    WriteByte(_Controls_Base_Address + _P1_Buttons_Offset, b);
                }
                else if (mouse.button == Win32.RI_MOUSE_LEFT_BUTTON_UP || mouse.button == Win32.RI_MOUSE_MIDDLE_BUTTON_UP)
                {
                    byte b = ReadByte(_Controls_Base_Address + _P1_Buttons_Offset);
                    b &= 0xFD;
                    WriteByte(_Controls_Base_Address + _P1_Buttons_Offset, b);
                }
            }
            else if (Player == 2)
            {
                //Write Axis
                WriteByte(_Controls_Base_Address + _P2_X_Offset, bufferX[0]);
                WriteByte(_Controls_Base_Address + _P2_Y_Offset, bufferY[0]);

                //Inputs
                if (mouse.button == Win32.RI_MOUSE_LEFT_BUTTON_DOWN || mouse.button == Win32.RI_MOUSE_MIDDLE_BUTTON_DOWN)
                {
                    byte b = ReadByte(_Controls_Base_Address + _P2_Buttons_Offset);
                    b |= 0x02;
                    WriteByte(_Controls_Base_Address + _P2_Buttons_Offset, b);
                }
                else if (mouse.button == Win32.RI_MOUSE_LEFT_BUTTON_UP || mouse.button == Win32.RI_MOUSE_MIDDLE_BUTTON_UP)
                {
                    byte b = ReadByte(_Controls_Base_Address + _P2_Buttons_Offset);
                    b &= 0xFD;
                    WriteByte(_Controls_Base_Address + _P2_Buttons_Offset, b);
                }
            }
        }

        #endregion
    }
}
