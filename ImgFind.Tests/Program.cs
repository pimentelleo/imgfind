using ImgFind;

var root = Path.Combine(Path.GetTempPath(), "imgfind-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TestScan();
    TestExcludeDuplicates();
    TestKeepDuplicatesAndRename();
    TestExistingDestinationDuplicate();
    TestPrepareProgress();
    TestPrepareProgressWithinFile();
    TestChangedOrMissingSource();
    Console.WriteLine("Todos os testes passaram.");
}
finally
{
    Directory.Delete(root, true);
}

void TestPrepareProgressWithinFile()
{
    var (source, destination) = CreateCase("progress-large");
    var path = Path.Combine(source, "big.png");
    File.WriteAllBytes(path, new byte[512 * 1024]);
    var updates = new List<PrepareProgress>();
    var plan = ImageOperations.Prepare(new[] { path }, destination, progress =>
    {
        updates.Add(progress);
        if (progress.Phase == "origem" && progress.Completed == 0 && progress.CurrentPath == "")
            Thread.Sleep(160);
    });
    Check(updates.Any(x => x.Phase == "origem" && x.Completed == 0 &&
                           x.BytesRead > 0 && x.CurrentSize == 512 * 1024),
          "Progresso dentro de um arquivo grande não foi informado.");
    Check(plan.Sources.Count == 1 && plan.Duplicates.Count == 0, "Arquivo único foi identificado como duplicata.");
}

void TestPrepareProgress()
{
    var (source, destination) = CreateCase("progress");
    var a = Path.Combine(source, "a.jpg");
    var b = Path.Combine(source, "b.png");
    File.WriteAllText(a, "identical");
    File.WriteAllText(b, "different");
    File.WriteAllText(Path.Combine(destination, "duplicate.txt"), "identical");
    File.WriteAllText(Path.Combine(destination, "other.txt"), "another size");
    var updates = new List<PrepareProgress>();
    var plan = ImageOperations.Prepare(new[] { a, b }, destination, updates.Add);
    Check(updates.Any(x => x.Phase == "origem" && x.Completed == 0 && x.Total == 2) &&
          updates.Any(x => x.Phase == "origem" && x.Completed == 2 && x.Total == 2),
          "Progresso da origem incorreto.");
    Check(updates.Any(x => x.Phase == "listar destino" && x.Total == null) &&
          updates.Any(x => x.Phase == "destino" && x.Completed == 1 && x.Total == 1) &&
          updates.Any(x => x.Phase == "agrupar" && x.Total == null),
          "Fases de progresso do destino incorretas.");
    Check(plan.Duplicates.Count == 1 && plan.Duplicates[0].Sources.Single() == a &&
          plan.Duplicates[0].ExistingFiles.Count == 1, "Agrupamento por conteúdo incorreto.");
    File.Delete(b);
    try
    {
        ImageOperations.Prepare(new[] { a, b }, destination, updates.Add);
        throw new Exception("A conferência deveria falhar para um arquivo ausente.");
    }
    catch (FileNotFoundException) { }
    Check(File.Exists(a) && File.Exists(Path.Combine(destination, "duplicate.txt")),
          "A falha da conferência alterou arquivos.");
}

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

(string Source, string Destination) CreateCase(string name)
{
    var source = Path.Combine(root, name, "source");
    var destination = Path.Combine(root, name, "destination");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(destination);
    return (source, destination);
}

void TestScan()
{
    var (source, _) = CreateCase("scan");
    Directory.CreateDirectory(Path.Combine(source, "nested"));
    var image = Path.Combine(source, "nested", "photo.JPG");
    File.WriteAllText(image, "image");
    File.WriteAllText(Path.Combine(source, "ignore.txt"), "not an image");
    var updates = new List<ScanProgress>();
    var result = ImageOperations.Scan(source, updates.Add);
    Check(result.Errors.Count == 0 && result.Images.SequenceEqual(new[] { image }), "A varredura recursiva falhou.");
    Check(updates.Count >= 2 && updates[0].CurrentDirectory == source && updates[0].ImagesFound == 0,
        "Progresso inicial não informado.");
    var last = updates[^1];
    Check(last.CurrentDirectory == Path.Combine(source, "nested") && last.DirectoriesVisited == 2 &&
          last.FilesExamined == 2 && last.ImagesFound == 1 && last.Errors == result.Errors.Count,
          "Contagens finais da varredura incorretas.");
}

void TestExcludeDuplicates()
{
    var (source, destination) = CreateCase("exclude");
    var a = Path.Combine(source, "a.jpg");
    var b = Path.Combine(source, "b.png");
    File.WriteAllText(a, "identical");
    File.WriteAllText(b, "identical");
    var plan = ImageOperations.Prepare(new[] { a, b }, destination);
    Check(plan.Duplicates.Count == 1 && plan.Duplicates[0].Sources.Count == 2, "Duplicatas com nomes diferentes não foram encontradas.");
    var result = ImageOperations.Move(plan, true);
    Check(result.Moved.Count == 1 && result.Skipped.Count == 1 && result.Errors.Count == 0, "Exclusão de duplicatas da operação falhou.");
    Check(File.Exists(b) && File.Exists(Path.Combine(destination, "a.jpg")), "Uma cópia deve permanecer na origem.");
}

void TestKeepDuplicatesAndRename()
{
    var (source, destination) = CreateCase("keep");
    Directory.CreateDirectory(Path.Combine(source, "nested"));
    var a = Path.Combine(source, "photo.jpg");
    var b = Path.Combine(source, "nested", "photo.jpg");
    File.WriteAllText(a, "same");
    File.WriteAllText(b, "same");
    File.WriteAllText(Path.Combine(destination, "photo.jpg"), "different");
    var result = ImageOperations.Move(ImageOperations.Prepare(new[] { a, b }, destination), false);
    Check(result.Moved.Count == 2 && result.Errors.Count == 0, "Não foi possível mover duplicatas.");
    Check(File.ReadAllText(Path.Combine(destination, "photo.jpg")) == "different"
        && File.ReadAllText(Path.Combine(destination, "photo (1).jpg")) == "same"
        && File.ReadAllText(Path.Combine(destination, "photo (2).jpg")) == "same", "Conflito de nomes sobrescreveu arquivo ou não foi resolvido.");
}

void TestExistingDestinationDuplicate()
{
    var (source, destination) = CreateCase("existing");
    var a = Path.Combine(source, "a.png");
    var existing = Path.Combine(destination, "different-name.jpg");
    File.WriteAllText(a, "same");
    File.WriteAllText(existing, "same");
    var plan = ImageOperations.Prepare(new[] { a }, destination);
    Check(plan.Duplicates.Count == 1 && plan.Duplicates[0].ExistingFiles.Contains(existing), "Duplicata no destino não foi encontrada.");
    var result = ImageOperations.Move(plan, true);
    Check(result.Moved.Count == 0 && result.Skipped.Count == 1 && File.Exists(a), "Duplicata no destino não foi preservada.");
    var self = ImageOperations.Move(ImageOperations.Prepare(new[] { existing }, destination), false);
    Check(self.Skipped.Count == 1 && File.Exists(existing), "Arquivo já no destino não foi preservado.");
}

void TestChangedOrMissingSource()
{
    var (source, destination) = CreateCase("changed");
    var a = Path.Combine(source, "a.jpg");
    var b = Path.Combine(source, "b.jpg");
    File.WriteAllText(a, "before");
    File.WriteAllText(b, "missing");
    var plan = ImageOperations.Prepare(new[] { a, b }, destination);
    File.WriteAllText(a, "AFTER!");
    File.Delete(b);
    var result = ImageOperations.Move(plan, false);
    Check(result.Errors.Count == 2 && result.Moved.Count == 0 && File.ReadAllText(a) == "AFTER!",
        "Erros ou alteração após conferência não foram tratados.");
}
