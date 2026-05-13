namespace Models;

public class Sala
{
    public int id { get; set; }
    public string name { get; set; }
    public string descricao { get; set; }
    
    public DateTime createdAt { get; set; }

    public Sala()
    {
        
    }
    
    public Sala(string name)
    {
        this.name = name;
        createdAt = DateTime.Now;
    }

    public Sala(string name, string descricao)
    {
        this.name = name;
        this.descricao = descricao;
        createdAt = DateTime.Now;
    }

    public Sala(int id, string name, string descricao, DateTime createdAt)
    {
        this.id = id;
        this.name = name;
        this.descricao = descricao;
        this.createdAt = createdAt;
    }
}