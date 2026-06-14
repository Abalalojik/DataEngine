namespace DBEngine.Model;

public class Model
{
    public class FieldData
    {
        private string _rawValue = string.Empty;
        private string? _cachedFormula;

        public string Name { get; set; } = string.Empty;
        public string RawValue 
        { 
            get => _rawValue; 
            set 
            {
                _rawValue = value ?? string.Empty;
                _cachedFormula = null;
            }
        }
        public bool IsFormula => _rawValue.StartsWith("FORMULA:");
        public string Formula 
        {
            get
            {
                if (!IsFormula) return string.Empty;
                return _cachedFormula ??= _rawValue["FORMULA:".Length..].Trim();
            }
        }
    }

    public class ObjectData
    {
        public string Collection { get; set; } = string.Empty;
        public List<FieldData> Fields { get; set; } = [];

        public FieldData? GetField(string name) =>
            Fields.FirstOrDefault(f => f.Name == name);
    }

    public record MixedFieldSignal
    {
        public string FieldName { get; set; } = string.Empty;
        public int DataCount { get; set; }
        public int FormulaCount { get; set; }
    }
}