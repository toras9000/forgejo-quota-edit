#r "nuget: Lestaly, 0.79.0"
#nullable enable
using Lestaly;
using Lestaly.Cx;

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-interact"), async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);

    WriteLine("テスト環境のリセット ...");
    var composeFile = ThisSource.RelativeFile("./docker/compose.yml");
    await "docker".args("compose", "--file", composeFile, "down", "--remove-orphans", "--volumes").result().success();
    await "docker".args("compose", "--file", composeFile, "up", "-d").result().success();
});
