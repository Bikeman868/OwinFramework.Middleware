using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Owin;

namespace OwinFramework.FormIdentification
{
    internal static class Net40Extensions
    {
        public static Task<IDictionary<string, string>> ReadFormAsync(this IOwinRequest request)
        {
            // application/x-www-form-urlencoded
            // multipart/form-data

            return Task.Factory.StartNew(() =>
            {
                var parser = new UrlencodedParser(request.Body);
                return parser.Variables;
            });
        }

        public class UrlencodedParser
        {
            public IDictionary<string, string> Variables { get; private set; }

            public UrlencodedParser(Stream stream)
            {
                Parse(stream, Encoding.UTF8);
            }

            public UrlencodedParser(Stream stream, Encoding encoding)
            {
                Parse(stream, encoding);
            }

            private void Parse(Stream stream, Encoding encoding)
            {
                var reader = new StreamReader(stream, encoding);
                var content = reader.ReadToEnd();

                /*
                 * https://www.w3.org/TR/html401/interact/forms.html
                 * 
                 * This is the default content type. Forms submitted with this content type must be encoded as follows:
                 * Control names and values are escaped. Space characters are replaced by `+', and then reserved characters are 
                 * escaped as described in [RFC1738], section 2.2: Non-alphanumeric characters are replaced by `%HH', a percent 
                 * sign and two hexadecimal digits representing the ASCII code of the character. Line breaks are represented 
                 * as "CR LF" pairs (i.e., `%0D%0A').
                 * The control names/values are listed in the order they appear in the document. The name is separated from the 
                 * value by `=' and name/value pairs are separated from each other by `&'.
                 */
                Func<string, string> decode = s =>
                {
                    return s.Replace('+', ' ');
                };

                Variables = content
                    .Split('&')
                    .Select(p =>
                    {
                        var e = p.Split('=');
                        return new
                        {
                            name = decode(e[0]),
                            value = decode(e[1])
                        };
                    })
                    .ToDictionary(e => e.name, e => e.value);
            }
        }

        public class MultipartParser
        {
            public MultipartParser(Stream stream)
            {
                Parse(stream, Encoding.UTF8);
            }

            public MultipartParser(Stream stream, Encoding encoding)
            {
                Parse(stream, encoding);
            }

            private void Parse(Stream stream, Encoding encoding)
            {
                this.Success = false;

                // Read the stream into a byte array
                var data = ToByteArray(stream);

                // Copy to a string for header parsing
                var content = encoding.GetString(data);

                // The first line should contain the delimiter
                var delimiterEndIndex = content.IndexOf("\r\n");
                if (delimiterEndIndex <= -1) return;

                var delimiter = content.Substring(0, content.IndexOf("\r\n"));

                // Look for Content-Type
                var re = new Regex(@"(?<=Content\-Type:)(.*?)(?=\r\n\r\n)");
                var contentTypeMatch = re.Match(content);

                // Look for filename
                re = new Regex(@"(?<=filename\=\"")(.*?)(?=\"")");
                var filenameMatch = re.Match(content);

                // Did we find the required values?
                if (!contentTypeMatch.Success || !filenameMatch.Success) return;

                // Set properties
                ContentType = contentTypeMatch.Value.Trim();
                Filename = filenameMatch.Value.Trim();

                // Get the start & end indexes of the file contents
                var startIndex = contentTypeMatch.Index + contentTypeMatch.Length + "\r\n\r\n".Length;

                var delimiterBytes = encoding.GetBytes("\r\n" + delimiter);
                var endIndex = IndexOf(data, delimiterBytes, startIndex);

                var contentLength = endIndex - startIndex;

                // Extract the file contents from the byte array
                var fileData = new byte[contentLength];

                Buffer.BlockCopy(data, startIndex, fileData, 0, contentLength);

                FileContents = fileData;
                Success = true;
            }

            private int IndexOf(byte[] searchWithin, byte[] serachFor, int startIndex)
            {
                var index = 0;
                var startPos = Array.IndexOf(searchWithin, serachFor[0], startIndex);
                if (startPos == -1) return -1;

                while ((startPos + index) < searchWithin.Length)
                {
                    if (searchWithin[startPos + index] == serachFor[index])
                    {
                        index++;
                        if (index == serachFor.Length)
                        {
                            return startPos;
                        }
                    }
                    else
                    {
                        startPos = Array.IndexOf<byte>(searchWithin, serachFor[0], startPos + index);
                        if (startPos == -1)
                        {
                            return -1;
                        }
                        index = 0;
                    }
                }

                return -1;
            }

            private byte[] ToByteArray(Stream stream)
            {
                var buffer = new byte[32768];
                using (var ms = new MemoryStream())
                {
                    while (true)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                            return ms.ToArray();
                        ms.Write(buffer, 0, read);
                    }
                }
            }

            public bool Success
            {
                get;
                private set;
            }

            public string ContentType
            {
                get;
                private set;
            }

            public string Filename
            {
                get;
                private set;
            }

            public byte[] FileContents
            {
                get;
                private set;
            }
        }
    }
}
