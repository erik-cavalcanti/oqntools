using System;
using System.IO;
namespace OQNTools
{
    public static class FilterTrace
    {
        public static void Mark(long id,string name)
        {
            try {
                var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OQN Tools");Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,"filtro-ultima-leitura.txt"),DateTime.Now.ToString("s")+"\nElemento: "+id+"\nParâmetro: "+name);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
