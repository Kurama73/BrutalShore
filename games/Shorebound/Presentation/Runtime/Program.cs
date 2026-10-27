using BrutalShore.Runtime;
using Shorebound;
using Shorebound.Application.Configuration;
using Shorebound.Infrastructure.Persistence;
using Shorebound.Presentation.Runtime;

// The entry point does nothing but turn a command line into a game, so that everything with a
// decision in it is in a type that can be read, changed and eventually tested on its own.
var options = Options.Parse(args);

// The player's settings, with whatever the command line said about them written over the top.
// Read here rather than inside the game because the host needs two of them — the interface scale
// and whether the overlay starts shown — before the game exists to be asked.
var settings = options.Override(SettingsFile.Read(options.SettingsPath));

// Order matters: the game holds pipelines and buffers created on the host's device, so it must be
// disposed first. Declaring the host first and the game second gets that right, because `using`
// disposes in reverse order of declaration.
using var host = new GameHost(options.HostWith(settings));
using var game = new ShoreboundGame(options, settings);

host.Run(game);
