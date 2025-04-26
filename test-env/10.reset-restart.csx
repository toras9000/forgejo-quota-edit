#r "nuget: Lestaly, 0.75.0"
#nullable enable
using Lestaly;
using Lestaly.Cx;

var noInteract = Args.Any(a => a == "--no-interact");
var pauseMode = noInteract ? PavedPause.None : PavedPause.Any;

return await Paved.RunAsync(config: c => c.PauseOn(pauseMode), action: async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);

    WriteLine("テスト環境のリセット ...");
    var composeFile = ThisSource.RelativeFile("./docker/compose.yml");
    var volumeDir = ThisSource.RelativeDirectory("./docker/volumes");
    await "docker".args("compose", "--file", composeFile, "down", "--remove-orphans", "--volumes").result().success();
    volumeDir.DeleteRecurse();
    await "docker".args("compose", "--file", composeFile, "up", "-d").result().success();
});
