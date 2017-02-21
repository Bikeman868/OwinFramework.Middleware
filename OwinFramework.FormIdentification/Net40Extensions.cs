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
            if (request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                return Task.Factory.StartNew(() => DecodeFormUrlEncoded(request.Body, Encoding.UTF8));

            if (request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException("Forms with multipart form data is not supported");

            throw new NotImplementedException("Unsupported content type " + request.ContentType);
        }

        private static IDictionary<string, string> DecodeFormUrlEncoded(Stream stream, Encoding encoding)
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
                s = s.Replace('+', ' ');
                var regex = new Regex("#[0-9A-F][0-9A-F]");
                return regex.Replace(s, m =>
                {
                    var hexDigits = m.Value.Substring(1);
                    var asciiValue = Convert.ToByte(hexDigits, 16);
                    var ch = (char)asciiValue;
                    return new String(new[] { ch });
                });
            };

            return content
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

        private static byte[] DecodeMultipartMimeEncoded(Stream stream, Encoding encoding)
        {
            var data = ToByteArray(stream);
            var content = encoding.GetString(data);

            var delimiterEndIndex = content.IndexOf("\r\n");
            if (delimiterEndIndex <= -1) return null;

            var delimiter = content.Substring(0, content.IndexOf("\r\n"));

            var re = new Regex(@"(?<=Content\-Type:)(.*?)(?=\r\n\r\n)");
            var contentTypeMatch = re.Match(content);

            re = new Regex(@"(?<=filename\=\"")(.*?)(?=\"")");
            var filenameMatch = re.Match(content);

            if (!contentTypeMatch.Success || !filenameMatch.Success) return null;

            var contentType = contentTypeMatch.Value.Trim();
            var filename = filenameMatch.Value.Trim();

            var startIndex = contentTypeMatch.Index + contentTypeMatch.Length + "\r\n\r\n".Length;
            var delimiterBytes = encoding.GetBytes("\r\n" + delimiter);
            var endIndex = IndexOf(data, delimiterBytes, startIndex);
            var contentLength = endIndex - startIndex;

            var fileData = new byte[contentLength];
            Buffer.BlockCopy(data, startIndex, fileData, 0, contentLength);

            return fileData;
        }

        private static byte[] ToByteArray(Stream stream)
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

        private static int IndexOf(byte[] searchWithin, byte[] serachFor, int startIndex)
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
    }
}
