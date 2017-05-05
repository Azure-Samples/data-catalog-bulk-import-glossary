using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace BulkImportGlossary
{
    public class Utility
    {
        public static List<GlossaryItem> ArrangeGlossaryItems(List<GlossaryItem> glossaryTerms)
        {
            Queue<GlossaryItem> queue = new Queue<GlossaryItem>();
            List<GlossaryItem> arrangedGlossaryItems = new List<GlossaryItem>();

            if (glossaryTerms == null)
            {
                return arrangedGlossaryItems;
            }

            // Enqueue all root terms
            foreach (var term in glossaryTerms)
            {
                if (term.ParentId == null)
                {
                    queue.Enqueue(term);
                }
            }

            // Iterate all terms using breath-first search
            while (queue.Count != 0)
            {
                GlossaryItem term = queue.Dequeue();
                foreach (var t in glossaryTerms)
                {
                    if (t.ParentId != null && t.ParentId.Equals(term.Id, StringComparison.InvariantCultureIgnoreCase))
                    {
                        queue.Enqueue(t);
                    }
                }
                arrangedGlossaryItems.Add(term);
            }

            return arrangedGlossaryItems;
        }

        public static void SaveIdUrlDictionary(Dictionary<string, string> idUrlDictionary, string dictPath)
        {
            // This will overwrite dict file if it exists
            File.WriteAllLines(dictPath, idUrlDictionary.Select(x => x.Key + " " + x.Value).ToArray());
        }

        public static Dictionary<string, string> LoadIdUrlDictionary(string dictPath)
        {
            var dict = new Dictionary<string, string>();
            foreach (string line in File.ReadLines(dictPath))
            {
                string key = line.Split(' ')[0];
                string value = line.Split(' ')[1];
                dict[key] = value;
            }
            return dict;
        }

        public static List<GlossaryItem> GetGlossaries(string csvPath)
        {
            List<GlossaryItem>  glossaries = new List<GlossaryItem>();

            using (TextFieldParser parser = new TextFieldParser(csvPath))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                while (!parser.EndOfData)
                {
                    // Process row
                    string[] fields = parser.ReadFields();

                    // Skip the first row and empty rows
                    if (fields == null || fields[0].Equals("ID", StringComparison.InvariantCultureIgnoreCase)) 
                        continue;

                    GlossaryItem glossaryItem = new GlossaryItem
                    {
                        Id = fields[0],
                        Name = fields[1],
                        ParentId = fields[2] == string.Empty ? null : fields[2],
                        Definition = fields[3],
                        Description = fields[4],
                        Stakeholders = GetStakeholders(fields[5].Split(';'))
                    };

                    glossaries.Add(glossaryItem);
                }
            }

            return glossaries;
        }

        public static IEnumerable<SecurityPrincipal> GetStakeholders(string[] upnObjectIdPairs)
        {
            foreach (string upnObjectIdPair in upnObjectIdPairs)
            {
                string[] upnObjectIdPairArray = upnObjectIdPair.Split('|');

                // Only consider stakeholders with both upn and object id
                if (upnObjectIdPairArray.Length != 2)
                {
                    continue;
                }

                var upn = upnObjectIdPairArray[0];
                var objectId = new Guid(upnObjectIdPairArray[1]);
                SecurityPrincipal securityPrincipal = new SecurityPrincipal(objectId, upn);
                yield return securityPrincipal;
            }
        }


    }
}
