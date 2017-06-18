﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using SharpGen.Logging;
using System.IO.Compression;
using SharpGen.MTPS;
using Newtonsoft.Json.Linq;

namespace SharpGen.Doc
{
    internal class MsdnProvider : IDocProvider
    {
        private static Regex stripSpace = new Regex(@"[\r\n]+\s+", RegexOptions.Multiline);
        private static Regex beginWithSpace = new Regex(@"^\s+");
        private Dictionary<Regex, string> mapReplaceName;
        private ZipArchive _zipFile;
        private bool isZipUpdated;
        private string archiveFullPath;

        private int filesAddedToArchive = 0;

        public MsdnProvider()
        {
            ArchiveName = "MSDNDoc.zip";
            UseArchive = true;
            mapReplaceName = new Dictionary<Regex, string>();
            ReplaceName("W::", @"::");
            ReplaceName("([a-z0-9])A::", @"$1::");
            ReplaceName("W$", @"");
            ReplaceName("^_+", @"");
        }

        public void ReplaceName(string fromNameRegex, string toName)
        {
            mapReplaceName.Add(new Regex(fromNameRegex), toName);
        }

        /// <summary>
        /// Archive to use to save the documentation
        /// </summary>
        public string ArchiveName { get; set; }

        /// <summary>
        /// Output path for the archive / Directory
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// Set to true to use a zip for caching documentation
        /// </summary>
        public bool UseArchive { get; set; }

        /// <summary>
        /// Begin to request MSDN
        /// </summary>
        public void Begin()
        {
            filesAddedToArchive = 0;
            string fullPath = (OutputPath ?? ".") + Path.DirectorySeparatorChar + ArchiveName;

            string outputDirectory = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            if (UseArchive)
            {
                archiveFullPath = outputDirectory + Path.DirectorySeparatorChar + ArchiveName;
                OpenArchive();
            }
        }

        /// <summary>
        /// End request to MSDN. Archive is saved if any updated occurred between Begin/End.
        /// </summary>
        public void End()
        {
            if (UseArchive)
            {
                CloseArchive();
            }
        }

        private void OpenArchive()
        {
            if (_zipFile == null)
            {
                isZipUpdated = false;
                var fileInfo = new FileInfo(archiveFullPath);
                if (fileInfo.Exists && fileInfo.Length > 0)
                {
                    _zipFile = ZipFile.Open(archiveFullPath, ZipArchiveMode.Update);
                }
                else
                {
                    File.Delete(archiveFullPath);
                    _zipFile = ZipFile.Open(archiveFullPath, ZipArchiveMode.Create);
                }
            }
        }

        private void CloseArchive(bool clone = false)
        {
            if (_zipFile != null)
            {
                _zipFile.Dispose();
                if (isZipUpdated && clone)
                    File.Copy(archiveFullPath, archiveFullPath + ".backup", true);
                _zipFile = null;
            }
        }

        private int counter = 0;


        /// <summary>
        /// Get the documentation for a particular prefix (include name) and a full name item
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public DocItem FindDocumentation(string name)
        {
            string oldName = name;
            // Regex replacer
            foreach (var keyValue in mapReplaceName)
            {
                if (keyValue.Key.Match(name).Success)
                {
                    name = keyValue.Key.Replace(name, keyValue.Value);
                    break;
                }
            }

            // Handle name with ends A or W
            if (name.EndsWith("A") || name.EndsWith("W"))
            {
                string previouewChar = new string(name[name.Length - 2], 1);

                if (previouewChar.ToUpper() != previouewChar)
                {
                    name = name.Substring(0, name.Length - 1);
                }
            }

            string doc = GetDocumentationFromCacheOrMsdn(name);
            if (doc == null)
            {
                return new DocItem() { Description = "No documentation for Direct3D12" };
            }
            return ParseDocumentation(doc);
        }

        /// <summary>
        /// Handles documentation from zip/directory
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetDocumentationFromCacheOrMsdn(string name)
        {
            string fileName = name.Replace("::", "-") + ".html";

            string doc;

            if (UseArchive)
            {
                OpenArchive();
                var zipEntry = _zipFile.GetEntry(fileName);
                if (zipEntry != null)
                {
                    using (var streamInput = zipEntry.Open())
                    using (var reader = new StreamReader(streamInput))
                    {
                        doc = reader.ReadToEnd();
                    }
                }
                else
                {
                    // Begin update if zip is not updated
                    if (!isZipUpdated)
                    {
                        isZipUpdated = true;
                    }

                    Logger.Progress(20 + (counter / 50) % 10, "Fetching C++ documentation ([{0}]) from MSDN", name);

                    doc = GetDocumentationFromMsdn(name);

                    var newEntry = _zipFile.CreateEntry(fileName);
                    using (var stream = newEntry.Open())
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(doc);
                    }

                    // Commit update every 20 files
                    filesAddedToArchive++;
                    if ((filesAddedToArchive % 20) == 0)
                    {
                        // Force a Flush of the archive
                        CloseArchive(true);
                    }
                }
            }
            else
            {
                fileName = OutputPath + Path.DirectorySeparatorChar + fileName;

                if (!File.Exists(fileName))
                {
                    Logger.Progress(20 + (counter / 50) % 10, "Fetching C++ documentation ([{0}]) from MSDN", name);

                    doc = GetDocumentationFromMsdn(name);
                    File.WriteAllText(fileName, doc);
                }
                else
                {
                    doc = File.ReadAllText(fileName);
                }
            }
            return doc;
        }


        private static HashSet<string> HtmlPreserveTags = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "dl", "dt", "dd", "p", "strong", "pre", "em", "code", "ul", "ol", "li", "table", "tr", "th", "td" };

        private static string ParseSubNodes(HtmlNode htmlNode, bool isRoot)
        {
            StringBuilder documentation = new StringBuilder();

            bool isDocClear = false;

            string htmlNodeName = htmlNode.Name.ToLower();
            if (HtmlPreserveTags.Contains(htmlNodeName))
                documentation.Append("<").Append(htmlNodeName).Append(">");
            
            if (htmlNode.Name == "a")
            {
                StringBuilder inside = new StringBuilder();
                foreach (var node in htmlNode.ChildNodes)
                    inside.Append(ParseSubNodes(node, false).Trim());
                string insideStr = inside.ToString();

                if (!string.IsNullOrEmpty(insideStr) && insideStr != "Copy")
                {
                    documentation.Append("{{");
                    insideStr = insideStr.Trim().Split(' ', '\t')[0];
                    documentation.Append(insideStr);
                    documentation.Append("}}");
                }
                return documentation.ToString();
            }
            else if (htmlNode.NodeType == HtmlNodeType.Text)
            {
                string text = htmlNode.InnerText;
                if (beginWithSpace.Match(text).Success)
                    text = beginWithSpace.Replace(text, " ");
                if (stripSpace.Match(text).Success)
                    text = stripSpace.Replace(text, " ");
                return text;
            }

            foreach (var node in htmlNode.ChildNodes)
            {
                string text = ParseSubNodes(node, false);

                if (text.StartsWith("Type:"))
                {
                    isDocClear = true;
                }
                else
                {
                    documentation.Append(text);
                }
            }
            
            if (!isDocClear)
            {
                if (HtmlPreserveTags.Contains(htmlNodeName))
                    documentation.Append("</").Append(htmlNodeName).Append(">");
            }

            if (isDocClear)
                documentation.Clear();

            return documentation.ToString();
        }

        private static Regex regexCapitals = new Regex(@"([^0-9A-Za-z_:\{])([A-Z][A-Z0-9_][0-9A-Za-z_:]*)");
        private static readonly Regex RegexReplacePointer = new Regex(@"pointer");


        /// <summary>
        /// Parse HtmlNode to extract a string from it. Replace anchors href with {{ }} 
        /// and code with [[ ]]
        /// </summary>
        /// <param name="htmlNode"></param>
        /// <returns></returns>
        private static string ParseNode(HtmlNode htmlNode)
        {
            var result = ParseSubNodes(htmlNode, true);
            result = regexCapitals.Replace(result, "$1{{$2}}");
            result = RegexReplacePointer.Replace(result, "reference");
            result = result.Trim();
            return result;
        }

        private static string GetTextUntilNextHeader(HtmlNode htmlNode, bool skipFirstNode = true, params string[] untilNodes)
        {
            if (skipFirstNode)
                htmlNode = htmlNode.NextSibling;

            while (htmlNode != null && htmlNode.Name.ToLower() == "div")
            {
                htmlNode = htmlNode.FirstChild;
            }
            if (htmlNode == null)
                return string.Empty;

            var builder = new StringBuilder();
            var nodes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "h3", "h2", "mtps:collapsiblearea" };
            foreach (var untilNode in untilNodes)
            {
                nodes.Add(untilNode);
            }

            while (htmlNode != null && !nodes.Contains(htmlNode.Name.ToLower()))
            {
                builder.Append(ParseNode(htmlNode));
                htmlNode = htmlNode.NextSibling;
            }

            return builder.ToString();
        }

        private static string ParseNextDiv(HtmlNode htmlNode)
        {
            while (htmlNode != null)
            {
                if (htmlNode.Name == "div" || htmlNode.Name.ToLower() == "mtps:collapsiblearea")
                    return ParseNode(htmlNode);
                htmlNode = htmlNode.NextSibling;
            }
            return "";
        }

        /// <summary>
        /// Parse a MSDN documentation file
        /// </summary>
        /// <param name="documentationToParse"></param>
        /// <returns></returns>
        public static DocItem ParseDocumentation(string documentationToParse)
        {
            if (string.IsNullOrEmpty(documentationToParse))
                return new DocItem();

            var htmlDocument = new HtmlDocument();
            //            htmlDocument.Load("Documentation\\d3d11-ID3D11Device-CheckCounter.html");
            htmlDocument.LoadHtml(documentationToParse);

            var item = new DocItem { Id = htmlDocument.DocumentNode.ChildNodes.FindFirst("id").InnerText };

            var element = htmlDocument.GetElementbyId("mainSection");

            // Page not found?
            if (element == null)
                return item;

            // Get description before h3/collasiblearea and table
            item.Description = GetTextUntilNextHeader(element.FirstChild, false, "table");

            HtmlNode firstElement = element.ChildNodes.FindFirst("dl");
            if (firstElement != null)
            {
                string termName = null;
                List<string> currentDoc = new List<string>();
                var nodes = firstElement.ChildNodes;
                foreach (HtmlNode htmlNode in nodes)
                {
                    if (htmlNode.Name == "dt")
                    {
                        if (currentDoc.Count > 0)
                        {
                            item.Items.Add(new DocSubItem
                            {
                                Term = termName,
                                Description = currentDoc[currentDoc.Count - 1]
                            });
                            currentDoc.Clear();
                            termName = htmlNode.InnerText;
                        }
                    }
                    else if (htmlNode.Name == "dd")
                    {
                        currentDoc.Add(ParseNode(htmlNode));
                    }
                }
                if (currentDoc.Count > 0)
                    item.Items.Add(new DocSubItem
                    {
                        Term = termName,
                        Description = currentDoc[currentDoc.Count - 1]
                    });
            }
            var headerCollection = element.SelectNodes("//h3 | //h2");
            if (headerCollection != null)
            {
                foreach (HtmlNode htmlNode in headerCollection)
                {
                    string text = ParseNode(htmlNode);
                    if (text.StartsWith("Remarks"))
                        item.Remarks = GetTextUntilNextHeader(htmlNode);
                    else if (text.StartsWith("Return"))
                        item.Return = GetTextUntilNextHeader(htmlNode);
                }
            }
            else
            {
                var returnCollection = element.SelectNodes("//h4[contains(.,'Return')]");
                if (returnCollection != null)
                    item.Return = ParseNextDiv(returnCollection[0].NextSibling);

                var remarksCollection = element.SelectNodes("//a[@id='remarks']");
                if (remarksCollection != null)
                {
                    item.Remarks = ParseNextDiv(remarksCollection[0].NextSibling);
                }
            }
            return item;
        }

        private static Regex regexUrlMoved = new Regex(@"This content has moved to\s+<a href=""(.*?)\""");

        /// <summary>
        /// Get MSDN documentation using an http query
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
        public static string GetDocumentationFromMsdn(string name)
        {

            var shortId = GetShortId(name);
            if (string.IsNullOrEmpty(shortId))
                return string.Empty;

            var result = GetDocFromMTPS(shortId);
            if (string.IsNullOrEmpty(result))
                return string.Empty;
            return "<id>" + shortId + "</id>\r\n" + result;
        }

        private static ContentServicePortTypeClient proxy;

        public static string GetDocFromMTPS(string shortId)
        {
            try
            {
                if (proxy == null)
                    proxy = new ContentServicePortTypeClient();

                var request = new getContentRequest
                {
                    contentIdentifier = shortId,
                    locale = "en-us",
                    version = "VS.85",
                    requestedDocuments = new[] { new requestedDocument() { type = documentTypes.primary, selector = "Mtps.Xhtml" } }
                };
                var response = proxy.GetContent(new appId() { value = "Sandcastle" }, request);
                if (response.primaryDocuments[0].Any != null)
                    return response.primaryDocuments[0].Any.OuterXml;
            }
            catch (Exception)
            {
            }
            return string.Empty;
        }



        private static Regex matchId = new Regex(@"/([a-zA-Z0-9\._\-]+)(\(.+\).*|\.[a-zA-Z]+)?$");

        public static string GetShortId(string name)
        {
            try
            {
                var url = "http://social.msdn.microsoft.com/Search/en-US?query=" + HttpUtility.UrlEncode(name) + "&addenglish=1";

                var result = GetFromUrl(url);

                if (string.IsNullOrEmpty(result))
                    return string.Empty;

                var resultsStart = "var results = ";
                var indexOfResults = result.IndexOf(resultsStart, StringComparison.Ordinal) + resultsStart.Length;
                if (indexOfResults > 0)
                {
                    var endOfLine = result.IndexOf('\n', indexOfResults) - 1;
                    var resultsText = result.Substring(indexOfResults, endOfLine - indexOfResults);
                    var endJsonSemicolon = resultsText.LastIndexOf(';');
                    var resultsJson = resultsText.Substring(0, endJsonSemicolon);
                    var urlResult = JObject.Parse(resultsJson);
                    var contentUrl = ((JArray)(urlResult["data"])["results"])[0]["url"].ToString();
                    var match = matchId.Match(contentUrl);
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        private static bool IsPageNotFound(string value)
        {
            return value.Contains("Page Not Found");
        }

        private static string GetFromUrlHandlingMove(string url)
        {
            string result = GetFromUrl(url);

            if (IsPageNotFound(result))
                return null;

            var matchMoved = regexUrlMoved.Match(result);
            if (matchMoved.Success)
            {
                result = GetFromUrl(matchMoved.Groups[1].Value);
            }

            if (IsPageNotFound(result))
                return null;

            return result;
        }

        internal static string GetFromUrl(string url)
        {
            try
            {
                // Create web request
                var request = (HttpWebRequest)WebRequest.Create(url);

                // Set value for request headers

                request.Method = "GET";
                request.ProtocolVersion = HttpVersion.Version11;
                request.AllowAutoRedirect = true;
                request.Accept = "*/*";
                request.UserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1; .NET CLR 2.0.50727)";
                request.Headers.Add("Accept-Language", "en-us");
                request.KeepAlive = true;

                StreamReader responseStream = null;
                HttpWebResponse webResponse = null;
                string webResponseStream = string.Empty;

                // Get response for http web request
                webResponse = (HttpWebResponse)request.GetResponse();
                responseStream = new StreamReader(webResponse.GetResponseStream());
                webResponseStream = responseStream.ReadToEnd();
                /*This content has moved to <a href=\"http://msdn.microsoft.com/en-us/library/microsoft.directx_sdk.reference.dideviceobjectinstance(v=VS.85).aspx?appId=Dev10IDEF1&amp;l=ENUS&amp;k=kDIDEVICEOBJECTINSTANCE);k(DevLang-&quot;C++&quot;);k(TargetOS-WINDOWS)&amp;rd=true\"
                                */
                return webResponseStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return string.Empty;
        }
    }
}