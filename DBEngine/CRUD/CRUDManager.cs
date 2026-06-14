using Mod = DBEngine.Model.Model;

namespace DBEngine.CRUD;

public class FetchResult
{
    public List<Mod.ObjectData> Records { get; set; } = [];
    public List<Mod.MixedFieldSignal> MixedFields { get; set; } = [];
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CRUDManager(string connectionString)
{
    private readonly string _connectionString = connectionString;

    public FetchResult FetchData(string sqlRequest, bool resolveFormula, bool signalMixedField)
    {
        throw new NotImplementedException();
    }
}