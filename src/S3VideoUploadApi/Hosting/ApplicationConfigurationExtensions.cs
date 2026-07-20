namespace S3VideoUploadApi.Hosting;

public static class ApplicationConfigurationExtensions
{
    public static WebApplicationBuilder ApplyEnvironmentConfiguration(
        this WebApplicationBuilder builder,
        string[] args)
    {
        // The UserSecretsId is shared by the project, so Development would normally
        // load the real-S3 bucket secret too. Reapply the LocalStack file afterward to
        // keep the two local modes isolated, then restore standard override precedence.
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile(
                "appsettings.Development.json",
                optional: false,
                reloadOnChange: true);
            RestoreOverridePrecedence(builder.Configuration, args);
        }

        // ASP.NET Core loads User Secrets automatically only for Development. LocalAws
        // deliberately does not load appsettings.Development.json (the LocalStack
        // endpoint), and environment/command-line values retain their normal priority.
        if (builder.Environment.IsEnvironment("LocalAws"))
        {
            builder.Configuration.AddUserSecrets<Program>(optional: true);
            RestoreOverridePrecedence(builder.Configuration, args);
        }

        return builder;
    }

    private static void RestoreOverridePrecedence(
        ConfigurationManager configuration,
        string[] args)
    {
        configuration.AddEnvironmentVariables();
        if (args.Length > 0)
        {
            configuration.AddCommandLine(args);
        }
    }
}
