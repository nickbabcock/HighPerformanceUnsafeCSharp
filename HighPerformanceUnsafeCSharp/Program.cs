using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;


namespace HighPerformanceUnsafeCSharp
{
    class Program
    {
        const int BUFFER_SIZE = 0x8000; //32 KB buffer
        const byte NEWLINE = (byte)'\n';
        const byte CARRIAGE_RETURN = (byte)'\r';
        const byte SPACE = (byte)' ';
        const int MAX_TOKEN_SIZE = 256;

        static readonly Encoding ENCODING = Encoding.GetEncoding(1252);

        static void Main(string[] args)
        {
            time(filestream, "Filestream");
            time(filestream2, "Filestream2");
            time(fixedFileStream, "Fixed Filestream");
            time(streamReader, "Streamreader");
            time(fixedStreamreader, "Fixed Streamreader");
            time(fixedStreamreader2, "Fixed Streamreader2");
            time(win32, "Win32");
            time(win32Safe, "Win32Safe");
        }
        static void time(Action<string, Action<string>> act, string actDescriptor)
        {
            string filePath = @"C:\Games\Paradox Interactive\Europa Universalis III\save games\test.eu3";
            Stopwatch watch = Stopwatch.StartNew();
            int count = 0;
            act(filePath, (s) => count += s.Length);
            watch.Stop();
            Console.WriteLine("{2} {0}: {1}", actDescriptor, watch.Elapsed.TotalSeconds, count);
        }

        static bool scannerNoMatch(byte c)
        {
            return c != NEWLINE && c != SPACE && c != CARRIAGE_RETURN;
        }

        static bool scannerNoMatch(char c)
        {
            return c != NEWLINE && c != SPACE && c != CARRIAGE_RETURN;
        }

        static bool scannerNoMatch(sbyte c)
        {
            return c != NEWLINE && c != SPACE && c != CARRIAGE_RETURN;
        }

        //Base method - most simple implementation
        static void filestream(string filePath, Action<string> callback)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            byte[] charBuffer = new byte[MAX_TOKEN_SIZE];
            int charIndex = 0;
            int bufferSize;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                do
                {
                    bufferSize = stream.Read(buffer, 0, BUFFER_SIZE);
                    for (int i = 0; i < bufferSize; i++)
                    {
                        if (scannerNoMatch(buffer[i]))
                        {
                            charBuffer[charIndex++] = buffer[i];
                        }
                        else
                        {
                            callback(ENCODING.GetString(charBuffer, 0, charIndex));
                            charIndex = 0;
                        }
                    }
                } while (bufferSize != 0);
            }
        }

        //This method improves upon the naive method (stringBuffer) as ENCODING.GetString
        //allocates a new character array with every invocation, and this method bypasses
        //this by reusing the same char array.  Surprisingly in tests, this method held
        //no improvement.
        static void filestream2(string filePath, Action<string> callback)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            byte[] charBuffer = new byte[MAX_TOKEN_SIZE];
            char[] encoderBuffer = new char[ENCODING.GetMaxCharCount(MAX_TOKEN_SIZE)];
            int charIndex = 0;
            int bufferSize, encodedChars;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                do
                {
                    bufferSize = stream.Read(buffer, 0, BUFFER_SIZE);
                    for (int i = 0; i < bufferSize; i++)
                    {
                        if (scannerNoMatch(buffer[i]))
                        {
                            charBuffer[charIndex++] = buffer[i];
                        }
                        else
                        {
                            encodedChars = ENCODING.GetChars(charBuffer, 0, charIndex, encoderBuffer, 0);
                            callback(new string(encoderBuffer, 0, encodedChars));
                            charIndex = 0;
                        }
                    }
                } while (bufferSize != 0);
            }
        }

        //Same as the base method, except the use of the buffer used to read the file,
        //which substitues 
        static unsafe void fixedFileStream(string filePath, Action<string> callback)
        {
            byte[] buffer = new byte[BUFFER_SIZE];

            int maxEncodeBuffer = ENCODING.GetMaxCharCount(MAX_TOKEN_SIZE);
            char* encodeBuffer = stackalloc char[maxEncodeBuffer];
            byte* charBuffer = stackalloc byte[MAX_TOKEN_SIZE];
            byte* charBufferStart = charBuffer;
            int bufferSize = 0, workingSize = 0;
            int charsDecoded = 0;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                do
                {
                    workingSize = bufferSize = stream.Read(buffer, 0, BUFFER_SIZE);
                    fixed (byte* bufferPtr = buffer)
                    {
                        byte* workingPtr = bufferPtr;
                        while (workingSize-- != 0)
                        {
                            if (scannerNoMatch(*workingPtr))
                            {
                                *charBuffer++ = *workingPtr++;
                            }
                            else
                            {
                                charsDecoded = ENCODING.GetChars(charBufferStart, (int)(charBuffer - charBufferStart), encodeBuffer, maxEncodeBuffer);
                                callback(new string(encodeBuffer, 0, charsDecoded));
                                charBuffer = charBufferStart;
                                workingPtr++;
                            }
                        }
                    }
                } while (bufferSize != 0);
            }
        }

        //Instead of manually dealing with bytes, instantiate a streamreader with the appropriate encoding.
        //This method is probably the most easy to understand and it is also the fastest.
        static void streamReader(string filePath, Action<string> callback)
        {
            char[] buffer = new char[BUFFER_SIZE];
            char[] charBuffer = new char[MAX_TOKEN_SIZE];
            int charIndex = 0;
            int bufferSize = 0;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, ENCODING))
            {
                do
                {
                    bufferSize = reader.Read(buffer, 0, BUFFER_SIZE);
                    for (int i = 0; i < bufferSize; i++)
                    {
                        if (scannerNoMatch(buffer[i]))
                        {
                            charBuffer[charIndex++] = buffer[i];
                        }
                        else
                        {
                            callback(new string(charBuffer, 0, charIndex));
                            charIndex = 0;
                        }
                    }
                } while (bufferSize != 0);
            }
        }



        static unsafe void fixedStreamreader(string filePath, Action<string> callback)
        {
            char[] buffer = new char[BUFFER_SIZE];
            char* charBuffer = stackalloc char[MAX_TOKEN_SIZE];
            char* charBufferStart = charBuffer;
            int bufferSize = 0, workingSize = 0;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, ENCODING))
            {
                fixed (char* bufferPtr = buffer)
                {
                    do
                    {
                        workingSize = bufferSize = reader.Read(buffer, 0, BUFFER_SIZE);
                        char* workingPtr = bufferPtr;
                        while (workingSize-- != 0)
                        {
                            if (scannerNoMatch(*workingPtr))
                            {
                                *charBuffer++ = *workingPtr++;
                            }
                            else
                            {
                                callback(new string(charBufferStart, 0, (int)(charBuffer - charBufferStart)));
                                charBuffer = charBufferStart;
                                workingPtr++;
                            }
                        }
                    } while (bufferSize != 0);
                }
            }
        }

        static unsafe void fixedStreamreader2(string filePath, Action<string> callback)
        {
            char[] buffer = new char[BUFFER_SIZE];
            char[] charBuffer = new char[MAX_TOKEN_SIZE];
            int bufferSize = 0, workingSize = 0;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, ENCODING))
            {
                fixed (char* bufferPtr = buffer)
                fixed (char* charBufferStart = charBuffer)
                {
                    char* wkrCharBuffer = charBufferStart;
                    do
                    {
                        workingSize = bufferSize = reader.Read(buffer, 0, BUFFER_SIZE);
                        char* workingPtr = bufferPtr;
                        while (workingSize-- != 0)
                        {
                            if (scannerNoMatch(*workingPtr))
                            {
                                *wkrCharBuffer++ = *workingPtr++;
                            }
                            else
                            {
                                callback(new string(charBufferStart, 0, (int)(wkrCharBuffer - charBufferStart)));
                                wkrCharBuffer = charBufferStart;
                                workingPtr++;
                            }
                        }
                    } while (bufferSize != 0);
                }
            }
        }

        const uint GENERIC_READ = 0x80000000;
        const uint OPEN_EXISTING = 3;
        static System.IntPtr handle;

        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern unsafe System.IntPtr CreateFile
        (
            string FileName,          // file name
            uint DesiredAccess,       // access mode
            uint ShareMode,           // share mode
            uint SecurityAttributes,  // Security Attributes
            uint CreationDisposition, // how to create
            uint FlagsAndAttributes,  // file attributes
            int hTemplateFile         // handle to template file
        );

        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
        static extern unsafe bool ReadFile
        (
            System.IntPtr hFile,      // handle to file
            void* pBuffer,            // data buffer
            int NumberOfBytesToRead,  // max number of bytes to read
            int* pNumberOfBytesRead,  // the number of bytes read
            int Overlapped            // overlapped buffer
        );

        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
        static extern unsafe bool CloseHandle(IntPtr hObject);

        static bool Open(string FileName)
        {
            // open the existing file for reading       
            handle = CreateFile
            (
                FileName,
                GENERIC_READ,
                0,
                0,
                OPEN_EXISTING,
                0,
                0
            );

            return handle != System.IntPtr.Zero;
        }

        //The majority of the win32 method is spent allocating an array of characters to hold the intermediate
        //of translating bytes to characters.  This method improves by performing the intermediate step on 
        //a single allocated char array.  This saves on many allocations and de-allocations.
        static unsafe void win32(string FilePath, Action<string> callback)
        {
            sbyte* buffer = stackalloc sbyte[BUFFER_SIZE];
            sbyte* startBuffer = buffer;
            sbyte* charBuffer = stackalloc sbyte[MAX_TOKEN_SIZE];
            sbyte* begCharBuffer = charBuffer;

            int maxTempCharBufferSize = ENCODING.GetMaxCharCount(MAX_TOKEN_SIZE);
            char* tempcharBuffer = stackalloc char[maxTempCharBufferSize];
            int bufferSize = 0, workingSize = 0, copiedChars = 0;
            Open(FilePath);

            do
            {
                ReadFile(handle, buffer, BUFFER_SIZE, &bufferSize, 0);
                workingSize = bufferSize;
                while (workingSize-- != 0)
                {
                    if (scannerNoMatch(*buffer))
                    {
                        *charBuffer++ = *buffer++;
                    }
                    else
                    {
                        copiedChars = ENCODING.GetChars((byte*)begCharBuffer, (int)(charBuffer - begCharBuffer), tempcharBuffer, maxTempCharBufferSize);
                        callback(new string(tempcharBuffer, 0, copiedChars));
                        charBuffer = begCharBuffer;
                        buffer++;
                    }
                }
                buffer = startBuffer;
            } while (bufferSize != 0);

            CloseHandle(handle);
        }


        //Equivalent to win32Buffer except everything is allocated with "new".
        //This tests the cost of fixed, heap allocation, and array traversal
        //when it comes to arrays allocated with stackalloc vs new.
        static unsafe void win32Safe(string FilePath, Action<string> callback)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            byte[] charBuffer = new byte[MAX_TOKEN_SIZE];

            int maxTempCharBufferSize = ENCODING.GetMaxCharCount(MAX_TOKEN_SIZE);
            char[] tempcharBuffer = new char[maxTempCharBufferSize];
            int bufferSize = 0, workingSize = 0, copiedChars = 0;
            Open(FilePath);

            fixed (byte* startBuffer = buffer, startCharBuffer = charBuffer)
            {
                byte* wkrBuffer = startBuffer;
                byte* wkrCharBuffer = startCharBuffer;
                do
                {
                    ReadFile(handle, startBuffer, BUFFER_SIZE, &bufferSize, 0);
                    workingSize = bufferSize;
                    while (workingSize-- != 0)
                    {
                        if (scannerNoMatch(*wkrBuffer))
                        {
                            *wkrCharBuffer++ = *wkrBuffer++;
                        }
                        else
                        {
                            copiedChars = ENCODING.GetChars(charBuffer, 0, (int)(wkrCharBuffer - startCharBuffer), tempcharBuffer, 0);
                            callback(new string(tempcharBuffer, 0, copiedChars));
                            wkrCharBuffer = startCharBuffer;
                            wkrBuffer++;
                        }
                    }
                    wkrBuffer = startBuffer;
                } while (bufferSize != 0);
            }
            CloseHandle(handle);
        }
    }
}
