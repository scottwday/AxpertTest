using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AxpertTest
{
    class Program
    {
        static MemoryStream _rxBuffer = new MemoryStream();
        static bool _gotResponse = false;

        enum ExitCode: int
        {
            Okay=0,
            InvalidArgument=1,
            NoResponse=2,
            ResponseTooShort=3,
            ResponseInvalidCrc = 4,

        }

        static void WriteUsage()
        {
            Console.WriteLine("Axpert reader");
            Console.WriteLine(" Usage:");
            Console.WriteLine("  axpert -p COM <-b [baud rate]> <-t [timeout ms]> command");
            Console.WriteLine();
            
        }

        /// <summary>
        /// Parses out command line args
        /// </summary>
        static string GetCommandLineArg(string[] args, string key)
        {
            string value = null;
            for (int i=0; i<args.Length-1; i++)
            {
                if (args[i] == key)
                    value = args[i + 1];
            }
            return value;
        }

        /// <summary>
        /// Appends crc and CR bytes to a byte array
        /// </summary>
        static byte[] GetMessageBytes(string text)
        {
            //Get bytes for command
            byte[] command = Encoding.ASCII.GetBytes(text);

            //Get CRC for command bytes
            ushort crc = CalculateCrc(command);

            //Append CRC and CR to command
            byte[] result = new byte[command.Length + 3];
            command.CopyTo(result, 0);
            result[result.Length - 3] = (byte)((crc >> 8) & 0xFF);
            result[result.Length - 2] = (byte)((crc >> 0) & 0xFF);
            result[result.Length - 1] = 0x0d;

            return result;
        }

        /// <summary>
        /// Calculates CRC for axpert inverter
        /// Ported from crc.c: http://forums.aeva.asn.au/forums/pip4048ms-inverter_topic4332_page2.html
        /// </summary>
        static ushort CalculateCrc(byte[] pin)
        {
            ushort crc;
            byte da;
            byte ptr;
            byte bCRCHign;
            byte bCRCLow;

            int len = pin.Length;

            ushort[] crc_ta = new ushort[]
                { 
                    0x0000,0x1021,0x2042,0x3063,0x4084,0x50a5,0x60c6,0x70e7,
                    0x8108,0x9129,0xa14a,0xb16b,0xc18c,0xd1ad,0xe1ce,0xf1ef
                };

            crc = 0;
            for (int index = 0; index < len; index++)
            {
                ptr = pin[index];

                da = (byte)(((byte)(crc >> 8)) >> 4);
                crc <<= 4;
                crc ^= crc_ta[da ^ (ptr >> 4)];
                da = (byte)(((byte)(crc >> 8)) >> 4);
                crc <<= 4;
                crc ^= crc_ta[da ^ (ptr & 0x0f)];
            }

            //Escape CR,LF,'H' characters
            bCRCLow = (byte)(crc & 0x00FF);
            bCRCHign = (byte)(crc >> 8);
            if (bCRCLow == 0x28 || bCRCLow == 0x0d || bCRCLow == 0x0a)
            {
                bCRCLow++;
            }
            if (bCRCHign == 0x28 || bCRCHign == 0x0d || bCRCHign == 0x0a)
            {
                bCRCHign++;
            }
            crc = (ushort)(((ushort)bCRCHign) << 8);
            crc |= bCRCLow;
            return crc;
        }

        static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            var sp = sender as SerialPort;

            if ((sp != null) && (!_gotResponse))
            {
                //Read chars until we hit a CR character
                while (sp.BytesToRead > 0)
                {
                    byte b = (byte)sp.ReadByte();
                    _rxBuffer.WriteByte(b);

                    if (b == 0x0d)
                    {
                        _gotResponse = true;
                        break;
                    }
                }
            }
        }

        static void DataErrorReceivedHandler(object sender, SerialErrorReceivedEventArgs e)
        {
            Console.Write(e.EventType);
        }

        static int Main(string[] args)
        {
            ExitCode exitCode = ExitCode.InvalidArgument;

            int baud;
            int.TryParse(GetCommandLineArg(args, "-b") ?? "2400", out baud);
            if ((baud < 1) || (baud > 2000000))
            {
                WriteUsage();
                Console.WriteLine("Invalid baud");
                return (int)ExitCode.InvalidArgument;
            }

            int timeoutMs;
            int.TryParse(GetCommandLineArg(args, "-t") ?? "1000", out timeoutMs);
            if ((timeoutMs < 1) || (timeoutMs > 30000))
            {
                WriteUsage();
                Console.WriteLine("Invalid timeout");
                return (int)ExitCode.InvalidArgument;
            }

            string comPort = GetCommandLineArg(args, "-p");
            if ((comPort == null) || (comPort.Length < 2) || (comPort.Length > 20))
            {
                WriteUsage();
                Console.WriteLine("Invalid com port");
                return (int)ExitCode.InvalidArgument;
            }

            string commandText = args.Last();
            if ((args.Length < 3) || (commandText.Length > 20))
            {
                WriteUsage();
                Console.WriteLine("No command provided");
                return (int)ExitCode.InvalidArgument;
            }

            SerialPort sp = new SerialPort();
            sp.PortName = comPort;
            sp.BaudRate = baud;
            sp.DataBits = 8;
            sp.Parity = Parity.None;
            sp.StopBits = StopBits.One;

            sp.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
            sp.ErrorReceived += new SerialErrorReceivedEventHandler(DataErrorReceivedHandler);
            
            sp.Open();

            byte[] commandBytes = GetMessageBytes(commandText);

            //Flush out any existing chars
            sp.ReadExisting();

            //Send request
            sp.Write(commandBytes, 0, commandBytes.Length);

            //Wait for response
            var startTime = DateTime.Now;
            while (!_gotResponse && ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs))
            {
                Thread.Sleep(20);
            }

            sp.Close();

            if (!_gotResponse)
                return (int)ExitCode.NoResponse;

            if (_rxBuffer.Length < 3)
                return (int)ExitCode.ResponseTooShort;

            byte[] payloadBytes = new byte[_rxBuffer.Length - 3];
            Array.Copy(_rxBuffer.GetBuffer(), payloadBytes, payloadBytes.Length);
            
            ushort crcMsb = _rxBuffer.GetBuffer()[_rxBuffer.Length - 3];
            ushort crcLsb = _rxBuffer.GetBuffer()[_rxBuffer.Length - 2];

            ushort calculatedCrc = CalculateCrc(payloadBytes);
            ushort receivedCrc = (ushort)((crcMsb << 8) | crcLsb);
            if (calculatedCrc != receivedCrc)
                return (int)ExitCode.ResponseInvalidCrc;

            //Write response to console
            Console.WriteLine(Encoding.ASCII.GetString(payloadBytes));
            
            return (int)ExitCode.Okay;
        }
    }
}
