using Models;
using Repository.Exceptions;
using Repository.Postgress;

namespace Repository;

public class Rodando
{
    public static async Task Main(string[] args)
    {
        DB.StartDatabase();    
        await DB.connect();
        await DB.Setup();   
        try
        {
        Usuario usuario = new Usuario("franciscogarbi", "Francisco");

        if (!await UsuarioDB.UserExists(usuario.username))
        {
            await UsuarioDB.Create(usuario);    
        }
        else
        {
            Console.WriteLine("Usuario já Existe");
        }
        
        

        List<Usuario> user = await UsuarioDB.Listar();

        foreach (Usuario u in user)
        {
            Console.WriteLine($"User {u.id}");
            Console.WriteLine(u.name);
            Console.WriteLine(u.username);
        }

        
        

            /*
            //Console.WriteLine($"ID: {sala.id}\nNome: {sala.name}\nDescrição: {sala.descricao}");
            Sala salaN = new Sala("5°C");
            SalaDB.Update(salaN, 1);
            List<Sala> salas = await SalaDB.ReadAll();
            int cont = 1;
            foreach (Sala sala in salas)
            {
                Console.WriteLine($"Sala {cont}:");
                Console.WriteLine($"id: {sala.id}");
                Console.WriteLine($"Nome: {sala.name}");
                Console.WriteLine($"Descrição: {sala.descricao}");
                Console.WriteLine("________________________________________-");
                cont++;
            }

            */

        }
        catch (DatabaseNotConnectedException e)
        {
            Console.WriteLine("Erro no banco: " + e.Message);
        }
        catch (ResourceNotFoundException e)
        {
            Console.WriteLine("Recurso não encontrado: " + e.Message);
        }
        catch (InvalidParameterException e)
        {
            Console.WriteLine("Parametro invalido: " + e.Message);
        }
        catch (ResourceAlreadyExistsException e)
        {
            Console.WriteLine(e.Message);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }
}