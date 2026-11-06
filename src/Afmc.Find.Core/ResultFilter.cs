namespace Afmc.Find.Core;
public sealed record ResultFilter(string Category="全部",long? MinBytes=null,long? MaxBytes=null,bool SeededOnly=false)
{
    public bool Matches(SearchResult row)=>(Category=="全部"||SearchQuality.Category(row)==Category)&&(!SeededOnly||row.Seeders>0)&&
        (MinBytes is null||row.Size>=MinBytes)&&(MaxBytes is null||row.Size<MaxBytes);
    public bool Active=>Category!="全部"||MinBytes is not null||MaxBytes is not null||SeededOnly;
}
