namespace FlatStructureBuilder
{
    /// <summary>
    /// This program builds elements from scratch in a flat structure. If the template does not already exist, it will create it.
    /// It will also create PI Points (linked to the PI-Random interface).
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            FlatBuilder flatBuilder = new FlatBuilder();
            flatBuilder.Run(args);
        }
    }
}
