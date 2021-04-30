using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using ConsoleTables;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace EfCoreSplitQueryPerformanceIssue
{
    class Program
    {
        public static bool loggingEnabled;

        static void Main(string[] args)
        {
            DiagnosticListener.AllListeners.Subscribe(new DiagnosticObserver());

            using var db = new MyDbContext();
            Console.WriteLine("EF Core Version {0}", db.Model.GetProductVersion());
            db.Database.OpenConnection();
            db.Database.EnsureCreated();

            var user = new User
            {
                UserName = "User 1",
                Hobbies = new List<Hobby>
                {
                    new Hobby {HobbyName = "Crocheting"},
                    new Hobby {HobbyName = "Beatboxing"},
                    new Hobby {HobbyName = "Witchcraft"}
                }
            };

            var participant1 = new Participant
            {
                ParticipantName = "Participant 1",
                CreatedBy = user
            };
            var participant2 = new Participant
            {
                ParticipantName = "Participant 2",
                CreatedBy = user
            };

            db.Add(new Event
            {
                EventName = "Event 1",
                Participants = new List<Participant>
                {
                    participant1,
                    participant2
                }
            });
            db.SaveChanges();

            loggingEnabled = true;
            var query =
                db.Events
                    .Include(x => x.Participants)
                    .ThenInclude(x => x.CreatedBy)
                    .ThenInclude(x => x.Hobbies)
                    .Where(x => x.EventId == 1);
            
            #if !EfCore2
            query = query.AsSplitQuery();
            #endif

            query.ToList();
        }
    }

    public class MyDbContext : DbContext
    {
        public DbSet<Event> Events { get; set; }
        public DbSet<Participant> Participants { get; set; }

        public DbSet<User> Users { get; set; }

        public DbSet<Hobby> Hobbies { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options
                .UseSqlite(@"Data Source=InMemorySample;Mode=Memory;Cache=Shared")
                .EnableSensitiveDataLogging();
    }

    public class Event
    {
        [Key]
        public long EventId { get; set; }

        public string EventName { get; set; }

        public List<Participant> Participants { get; set; }
    }

    public class Participant
    {
        [Key]
        public long ParticipantId { get; set; }

        public string ParticipantName { get; set; }

        public long EventId { get; set; }
        public Event Event { get; set; }

        public long CreatedById { get; set; }
        public User CreatedBy { get; set; }
    }

    public class User
    {
        [Key]
        public long UserId { get; set; }

        public string UserName { get; set; }

        public List<Hobby> Hobbies { get; set; }
    }

    public class Hobby
    {
        [Key]
        public long HobbyId { get; set; }

        public string HobbyName { get; set; }
    }

    public class DiagnosticObserver : IObserver<DiagnosticListener>
    {
        public void OnCompleted()
            => throw new NotImplementedException();

        public void OnError(Exception error)
            => throw new NotImplementedException();

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name) // "Microsoft.EntityFrameworkCore"
            {
                value.Subscribe(new KeyValueObserver());
            }
        }
    }

    public class KeyValueObserver : IObserver<KeyValuePair<string, object>>
    {
        public void OnCompleted()
            => throw new NotImplementedException();

        public void OnError(Exception error)
            => throw new NotImplementedException();

        public void OnNext(KeyValuePair<string, object> value)
        {
            if (!Program.loggingEnabled)
            {
                return;
            }

            if (value.Key == RelationalEventId.CommandExecuted.Name)
            {
                // Custom console logger to also output query results into console to be able to highlight the issue better
                var payload = (CommandExecutedEventData) value.Value;

                using var connection = new SqliteConnection(@"Data Source=InMemorySample;Mode=Memory;Cache=Shared");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = payload.Command.CommandText;
                using var reader = command.ExecuteReader();

                var enumerator = reader.GetEnumerator();
                Console.WriteLine(payload.ToString());
                Console.WriteLine();

                Console.WriteLine("Results:");

                var columnNames = Enumerable.Range(0, reader.FieldCount).Select(x => reader.GetName(x)).ToArray();

                var table = new ConsoleTable(columnNames);
                while (enumerator.MoveNext())
                {
                    var current = enumerator.Current as DbDataRecord;
                    table.AddRow(columnNames.Select((_, i) => current.GetValue(i)).ToArray());
                }

                table.Write();
                Console.WriteLine();
            }
        }
    }
}
