﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EPiServer.Data.Dynamic;
using log4net;
using PageTypeBuilder.Reflection;
using PageTypeBuilder.Synchronization.Hooks;

namespace PageTypeBuilder.Migrations
{
    public class MigrationsHook : IPreSynchronizationHook
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MigrationsHook));

        public void PreSynchronization(ISynchronizationHookContext context)
        {
            log.Debug("Migration hook executing");
            if (Settings.GetConfiguration().Disabled)
            {
                log.Debug("Migrations has been disabled through configuration. Discontinuing migrations.");
                return;
            }

            var migrations = FindMigrations(context.AssemblyLocator)
                .Select(type => (IMigration) Activator.CreateInstance(type));
            ValidateMigrations(migrations);
            ExecuteMigrations(migrations);
        }

        IEnumerable<Type> FindMigrations(IAssemblyLocator assemblyLocator)
        {
            return assemblyLocator.AssembliesWithReferenceToAssemblyOf<IMigration>()
                .TypesAssignableTo<IMigration>().Concrete();
        }

        static Regex validMigrationPattern = new Regex("Migration[1-9][0-9]*$", RegexOptions.Compiled);

        void ValidateMigrations(IEnumerable<IMigration> migrations)
        {
            var types = migrations.Select(m => m.GetType());
            ValidateAllInSameNamespace(types);
            ValidateNames(types);
            ValidateNamesInConsecutiveOrder(migrations);

            var numbersOfExecuted = GetStore().Items<ExecutedMigration>().ToList().Select(m => m.Number);
            var numbersOfMigrationsInAppDomain = migrations.Select(m => m.Number());
            var migrationsNotInAppDomain = numbersOfExecuted
                .Where(m => !numbersOfMigrationsInAppDomain.Contains(m));
            if(migrationsNotInAppDomain.Count() > 0)
            {
                var errorMessage = "Unable to find one or more executed migrations in the application domain."
                                   +
                                   " This may be caused by someone else using the same database having created a migration."
                                   + " Try getting the latest source code. The migrations are: "
                                   + migrationsNotInAppDomain.Select(m => m.ToString())
                                         .Aggregate((accumulate, name) => accumulate + ", " + name)
                                   + ". Although not advisable, you can also disable migrations in web.config.";
                throw new PageTypeBuilderException(errorMessage);
            }
        }

        void ValidateNames(IEnumerable<Type> migrations)
        {
            var withInvalidNames = migrations.Where(migration => !validMigrationPattern.IsMatch(migration.Name));
            if(withInvalidNames.Count() > 0)
            {
                var errorMessage = string.Format(
                    "Migration(s) with invalid name found: {0}. " 
                    + "Migrations should be named MigrationX where X is " 
                    + "a number which doesn't start with 0.",
                    withInvalidNames.Select(type => type.Name)
                        .Aggregate((accumulate, name) => accumulate + ", " + name));
                throw new PageTypeBuilderException(errorMessage);
            }
        }

        void ValidateAllInSameNamespace(IEnumerable<Type> migrations)
        {
            if (migrations.GroupBy(m => m.Namespace).Count() > 1)
            {
                string errorMessage = "Migrations found in multiple namespaces. "
                                      + "All concrete classes implementing IMigration"
                                      + "must reside in the same namespaces.";
                throw new PageTypeBuilderException(errorMessage);
            }
        }

        void ValidateNamesInConsecutiveOrder(IEnumerable<IMigration> migrations)
        {
            if(migrations.Count() == 0)
            {
                return;
            }

            var migrationNumbers = migrations.Select(x => x.Number()).OrderBy(x => x);
            var comparisonNumbers = Enumerable.Range(migrationNumbers.Min(), migrationNumbers.Max());
            if(migrationNumbers.SequenceEqual(comparisonNumbers))
            {
                return;
            }

            string errorMessage = "All migrations must be numbered in a consecutive order."
                + " Found: " + migrationNumbers.Select(x => x.ToString()).Aggregate((accumulate, number) => accumulate + ", " + number)
                + " Expected: " + comparisonNumbers.Select(x => x.ToString()).Aggregate((accumulate, number) => accumulate + ", " + number);
            throw new PageTypeBuilderException(errorMessage);
        }

        void ExecuteMigrations(IEnumerable<IMigration> migrations)
        {
            var store = GetStore();
            var lastApplied = store.Items<ExecutedMigration>()
                .OrderByDescending(migration => migration.Number)
                .Take(1)
                .ToList()
                .Select(migration => migration.Number)
                .FirstOrDefault();
            log.Debug("Last migration executed according to database was " + lastApplied);

            migrations.Where(migration => migration.Number() > lastApplied)
                .ToList().ForEach(migration =>
                    {
                        log.Debug("Executing migration " + migration.Number());
                        migration.Execute();
                        var executed = new ExecutedMigration(migration);
                        store.Save(executed);
                    });
        }

        static DynamicDataStore GetStore()
        {
            var type = typeof (ExecutedMigration);
            var storeName = "PageTypeBuilderMigrations";
            return DynamicDataStoreFactory.Instance.GetStore(storeName) ??
                DynamicDataStoreFactory.Instance.CreateStore(storeName, type);
        }
    }
}
