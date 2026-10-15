namespace System.Runtime.InteropServices.CommandLine
{
    internal sealed class ManagedEntryPointArgumentSource(
        ICommandLineArgumentSource commandLineArguments) :
        IManagedEntryPointArgumentSource
    {
        private readonly ICommandLineArgumentSource _commandLineArguments =
            commandLineArguments ?? throw new ArgumentNullException();

        public string[] Read()
        {
            var commandLineArguments = _commandLineArguments.Read();
            if (commandLineArguments.Length <= 1)
            {
                return [];
            }

            var arguments = new string[commandLineArguments.Length - 1];
            for (var index = 0; index < arguments.Length; index++)
            {
                arguments[index] = commandLineArguments[index + 1];
            }
            return arguments;
        }
    }
}
