using System;
using System.Collections.Generic;
using System.Text;

namespace MesabrookAPIQueryTool
{
    public class QueryResponseObject
    {
        public QueryResponseObject() { }

        public QueryResponseObject(KeyValuePair<string, object?> kvp)
        {
            Key = kvp.Key;
            Value = kvp.Value;

            if (Value is IDictionary<string, object?> subDict)
            {
                Children = subDict.Select(kvp => new QueryResponseObject(kvp)).ToList();
            }
        }

        public string Key { get; set; }
        public object? Value { get; set; }
        public string FormattedValue => Value switch
        {
            null => "null",
            DateTime dt => dt.ToString("MM/dd/yyyy HH:mm:ss"),
            _ => Value?.ToString() ?? string.Empty
        };

        public bool HasChildren => Children != null && Children.Any();
        public List<QueryResponseObject>? Children { get; set; }
    }
}
