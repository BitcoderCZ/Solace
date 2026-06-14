using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Solace.DB.Utils;

public static class DatabaseFacadeExtensions
{
    extension(DatabaseFacade database)
    {
        public DatabaseProvider Provider => database.ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => DatabaseProvider.Sqlite,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => DatabaseProvider.Postgres,
            _ => throw new InvalidOperationException($"The database provider '{database.ProviderName}' is not supported.")
        };
    }
}