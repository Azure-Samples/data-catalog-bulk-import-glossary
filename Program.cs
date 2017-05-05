// Microsoft Azure Data Catalog team sample, import glossary terms in batch

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Net;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BulkImportGlossary
{
    /// <summary>
    /// This sample demonstrates how to load terms into the Azure Data Catalog business glossary using
    /// the Data Catalog REST API. The sample loads term definitions from a CSV file and creates the 
    /// terms in the catalog. If a term already exists in the catalog, the sample can overwrite the term 
    /// definition to update the term in the catalog.
    /// 
    /// A sample input file is included with the sample code. The CSV file requires the following columns:
    /// * Term ID (a surrogate key required for the import file to disambiguate terms in different hierarchies, and to relate terms to parent terms)
    /// * Term (required)
    /// * Parent Term ID (optional - references the Term ID value for a previously-defined term)
    /// * Definition (required)
    /// * Description (optional)
    /// * Stakeholders (optional)
    ///
    /// The Stakeholders column assumes a semicolon-delimited list of email addresses (UPNs) and AAD ObjectIDs.
    /// Example:
    /// user1@example.com|cf3092bd-53a1-41c3-8831-1aec571958f7;user2@example.com|c0bf23bd-beef-beef-beef-beefbad95bad
    /// The AAD ObjectID value can be obtained by using the Get-AzureADUser PowerShell cmdlet - see 
    /// https://docs.microsoft.com/powershell/module/azuread/get-azureaduser?view=azureadps-2.0
    /// </summary>
    public class Program
    {
        // TODO: Replace the Client ID placeholder with a client ID authorized to access your Azure Active Directory
        // To learn how to register a client app and get a Client ID, see https://msdn.microsoft.com/library/azure/mt403303.aspx
        private const string ClientIdFromAzureAppRegistration = "PLACEHOLDER";

        // TODO: Replace the catalog name placeholder with the name of your catalog
        private const string CatalogName = "PLACEHOLDER";
        

        // Note: Set this to true to automatically overwrite all existing glossary terms without prompting
        private const bool OverWriteAll = true;

        private const string GlossaryItemFileName = "GlossaryItem.csv";
        private static readonly string CurrentDirectory = Directory.GetCurrentDirectory();

        static AuthenticationResult _authResult;
        static Dictionary<string, string> _idUrlDictionary = new Dictionary<string, string>();

        static Dictionary<string, List<string>> _existingTermsParentChildrenDictionary = new Dictionary<string, List<string>>();
        static Dictionary<string, string> _existingTermsIdNameDictionary = new Dictionary<string, string>();
        static Dictionary<string, GlossaryItem> _newTermsIdNameDictionary = new Dictionary<string, GlossaryItem>();

        static void Main()
        {
            string csvPath = String.Format(@"{0}\{1}", CurrentDirectory, GlossaryItemFileName);

            List<GlossaryItem> existingTerms = EnumerateGlossaryTerm();
            BuildMapsForExistingTerms(existingTerms);

            List<GlossaryItem> newTerms = Utility.GetGlossaries(csvPath);
            BuildMapsForTermsToPublish(newTerms);

            List<GlossaryItem> arrangedTerms = Utility.ArrangeGlossaryItems(newTerms);
            if (arrangedTerms.Count != newTerms.Count)
            {
                Console.WriteLine("Error: invalid glossary term hierarchy. Please ensure each term's parent term is present in the input file. Press Enter to exit.");
                Console.ReadLine();
                Environment.Exit(-1);
            }

            newTerms = arrangedTerms;

            ConsoleKey key;

            foreach (var newTerm in newTerms)
            {
                var jsonObjectPayload = JObject.Parse(newTerm.Serialize());
                jsonObjectPayload.Remove("id");

                if (newTerm.ParentId != null)
                {
                    jsonObjectPayload["parentId"] = _idUrlDictionary[newTerm.ParentId];
                }
                
                string jsonPayload = jsonObjectPayload.ToString();
                string url;

                if (TermAlreadyExists(newTerm, out url))
                {
                    if (!OverWriteAll)
                    {
                        Console.WriteLine("Glossary term {0} already exists, do you want to update it? [y/n]", string.Join("->", GetNamePathForNewTerms(newTerm.Id)));
                        key = Console.ReadKey().Key;
                        Console.WriteLine();
                    }

                    if (OverWriteAll || key.Equals(ConsoleKey.Y))
                    {
                        url = UpsertGlossaryTerm(jsonPayload, url);
                        Console.WriteLine("Updated glossary term: {0}, term url: {1}", newTerm.Name, url);
                    }
                }
                else
                {
                    url = UpsertGlossaryTerm(jsonPayload);
                    Console.WriteLine("Pulished glossary term: {0}, term url: {1}", newTerm.Name, url);
                }
                _idUrlDictionary[newTerm.Id] = url;
            }

            Console.WriteLine("Bulk importing glossary terms completed. Press Enter to continue.");
            Console.ReadLine();

            // Optionally clean up published/updated terms during testing
            Console.WriteLine("Delete published/updated terms? [y/n]");
            key = Console.ReadKey().Key;
            Console.WriteLine();
            if (key.Equals(ConsoleKey.Y))
            {
                List<int> ids = _idUrlDictionary.Keys.Select(int.Parse).ToList();
                ids.Sort();
                ids.Reverse();

                foreach (int id in ids)
                {
                    var deleteResult = DeleteGossaryTerm(_idUrlDictionary[id.ToString()]);
                    if (deleteResult.Equals("NoContent", StringComparison.InvariantCultureIgnoreCase))
                    {
                        Console.WriteLine("Deleted {0}", _idUrlDictionary[id.ToString()]);
                        _idUrlDictionary.Remove(id.ToString());
                    }
                }

                Console.WriteLine("Deleted published/updated terms. Press Enter to continue.");
                Console.ReadLine();
            }
        }
		
        static void BuildMapsForExistingTerms(List<GlossaryItem> existingTerms)
        {
            if (existingTerms == null)
            {
                return;
            }

            foreach (var term in existingTerms)
            {
                _existingTermsIdNameDictionary[term.Id] = term.Name;

                var parentId = term.ParentId ?? string.Empty;

                if (!_existingTermsParentChildrenDictionary.ContainsKey(parentId))
                {
                    _existingTermsParentChildrenDictionary[parentId] = new List<string>();
                }
                _existingTermsParentChildrenDictionary[parentId].Add(term.Id);
            }
        }

        static void BuildMapsForTermsToPublish(List<GlossaryItem> glossaryTerms)
        {
            glossaryTerms.ForEach(t => _newTermsIdNameDictionary[t.Id] = t);
        }

        static List<string> GetNamePathForNewTerms(string id)
        {
            List<string> path = new List<string>();
            path.Insert(0, _newTermsIdNameDictionary[id].Name);
            while (_newTermsIdNameDictionary[id].ParentId != null)
            {
                id = _newTermsIdNameDictionary[id].ParentId;
                path.Insert(0, _newTermsIdNameDictionary[id].Name);
            }
            return path;
        }

        static bool TermAlreadyExists(GlossaryItem term, out string termLocation)
        {
            termLocation = null;

            if (_existingTermsParentChildrenDictionary.Count == 0)
            {
                return false;
            }

            List<string> path = GetNamePathForNewTerms(term.Id);
            List<string> currentLevel = _existingTermsParentChildrenDictionary[string.Empty];
            foreach (string name in path)
            {
                bool match = false;
                if (currentLevel == null)
                {
                    termLocation = null;
                    return false;
                }

                foreach (var id in currentLevel)
                {
                    if (_existingTermsIdNameDictionary[id].Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    {
                        currentLevel = _existingTermsParentChildrenDictionary.ContainsKey(id) ? _existingTermsParentChildrenDictionary[id] : null;
                        match = true;
                        termLocation = id;
                        break;
                    }
                }

                if (!match)
                {
                    termLocation = null;
                    return false;
                }
            }

            return true;
        }

        static List<GlossaryItem> EnumerateGlossaryTerm()
        {
            string fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/glossaries/{0}/terms?api-version=2016-03-30", CatalogName);

            // Create a GET WebRequest as a Json content type
            HttpWebRequest request = WebRequest.Create(fullUri) as HttpWebRequest;
            if (request != null)
            {
                request.KeepAlive = true;
                request.Method = "GET";
                request.Accept = "application/json;adc.metadata=full";
            }

            try
            {
                var response = SetRequestAndGetResponse(request);
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var itemPayload = reader.ReadToEnd();
                    // Console.WriteLine(itemPayload);
                    JToken terms;
                    JObject.Parse(itemPayload).TryGetValue("value", out terms);
                    if (terms != null)
                    {
                        return JsonConvert.DeserializeObject<List<GlossaryItem>>(terms.ToString());
                    }
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.Status);
                if (ex.Response != null)
                {
                    // can use ex.Response.Status, .StatusDescription
                    if (ex.Response.ContentLength != 0)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        {
                            if (stream != null)
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Console.WriteLine(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        // Delete glossary term
        static string DeleteGossaryTerm(string termLocation, string etag = null)
        {
            string responseStatusCode;
            string fullUri = string.Format("{0}?api-version=2016-03-30", termLocation);

            HttpWebRequest request = WebRequest.Create(fullUri) as HttpWebRequest;
            if (request != null)
            {
                request.KeepAlive = true;
                request.Method = "DELETE";

                if (etag != null)
                {
                    request.Headers.Add("If-Match", string.Format(@"W/""{0}""", etag));
                }
            }

            try
            {
                using (HttpWebResponse response = SetRequestAndGetResponse(request))
                {
                    responseStatusCode = response.StatusCode.ToString();
                }
            }
            catch (WebException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.Status);
                if (ex.Response != null)
                {
                    // can use ex.Response.Status, .StatusDescription
                    if (ex.Response.ContentLength != 0)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        {
                            if (stream != null)
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Console.WriteLine(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                }
                return null;
            }

            return responseStatusCode;
        }


        // Create or update glossary term
        static string UpsertGlossaryTerm(string json, string termLocation = null)
        {
            string fullUri;
            HttpWebRequest request;
            string termLocationReturned;

            if (string.IsNullOrEmpty(termLocation)) // Post request if no term location provided
            {
                fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/glossaries/{0}/terms?api-version=2016-03-30", CatalogName);
                request = WebRequest.Create(fullUri) as HttpWebRequest;
                if (request != null)
                {
                    request.Method = "POST";
                    request.KeepAlive = true;
                }
            }
            else // PUT request if term location provided
            {
                fullUri = string.Format("{0}?api-version=2016-03-30", termLocation);
                request = WebRequest.Create(fullUri) as HttpWebRequest;
                if (request != null)
                {
                    request.Method = "PUT";
                    request.KeepAlive = true;
                }
            }

            try
            {
                var response = SetRequestAndGetResponse(request, json);

                // Get the Response header which contains the glossary term Uri
                termLocationReturned = response.Headers["Location"];
            }
            catch(WebException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.Status);
                if (ex.Response != null)
                {
                    // Can use ex.Response.Status, .StatusDescription
                    if (ex.Response.ContentLength != 0)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        {
                            if (stream != null)
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Console.WriteLine(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                }
                return null;
            }

            return termLocationReturned;
        }

        static HttpWebResponse SetRequestAndGetResponse(HttpWebRequest request, string payload = null)
        {
            while (true)
            {
                // To authorize the operation call, you need an access token which is part of the Authorization header
                request.Headers.Add("Authorization", AccessToken().Result.CreateAuthorizationHeader());
                // Set to false to be able to intercept redirects
                request.AllowAutoRedirect = false;

                if (!string.IsNullOrEmpty(payload))
                {
                    byte[] byteArray = Encoding.UTF8.GetBytes(payload);
                    request.ContentLength = byteArray.Length;
                    request.ContentType = "application/json";
                    // Write JSON byte[] into a Stream
                    request.GetRequestStream().Write(byteArray, 0, byteArray.Length);
                }
                else
                {
                    request.ContentLength = 0;
                }

                HttpWebResponse response = request.GetResponse() as HttpWebResponse;

                if (response != null && response.StatusCode == HttpStatusCode.Redirect)
                {
                    string redirectedUrl = response.Headers["Location"];
                    HttpWebRequest nextRequest = WebRequest.Create(redirectedUrl) as HttpWebRequest;
                    if (nextRequest != null)
                    {
                        nextRequest.Method = request.Method;
                        request = nextRequest;
                    }
                }
                else
                {
                    return response;
                }
            }
        }

        // Get access token:
        // To call a Data Catalog REST operation, create an instance of AuthenticationContext and call AcquireToken
        // AuthenticationContext is part of the Active Directory Authentication Library NuGet package
        // To install the Active Directory Authentication Library NuGet package in Visual Studio, 
        // run "Install-Package Microsoft.IdentityModel.Clients.ActiveDirectory" from the NuGet Package Manager Console.
        static async Task<AuthenticationResult> AccessToken()
        {
            if (_authResult == null)
            {
                // Resource Uri for Data Catalog API
                string resourceUri = "https://api.azuredatacatalog.com";

                // To learn how to register a client app and get a Client ID, see https://msdn.microsoft.com/en-us/library/azure/mt403303.aspx#clientID   
                string clientId = ClientIdFromAzureAppRegistration;

                // A redirect uri gives AAD more details about the specific application that it will authenticate.
                // Since a client app does not have an external service to redirect to, this Uri is the standard placeholder for a client app.
                string redirectUri = "https://login.live.com/oauth20_desktop.srf";

                // Create an instance of AuthenticationContext to acquire an Azure access token
                // OAuth2 authority Uri
                string authorityUri = "https://login.windows.net/common/oauth2/authorize";
                AuthenticationContext authContext = new AuthenticationContext(authorityUri);

                // Call AcquireToken to get an Azure token from Azure Active Directory token issuance endpoint
                // AcquireToken takes a Client Id that Azure AD creates when you register your client app.
                _authResult = await authContext.AcquireTokenAsync(resourceUri, clientId, new Uri(redirectUri), new PlatformParameters(PromptBehavior.Always));
            }

            return _authResult;
        }
    }
}
