using ImgFind;

var root = Path.Combine(Path.GetTempPath(), "imgfind-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TestScan();
    TestExcludeDuplicates();
    TestKeepDuplicatesAndRename();
    TestExistingDestinationDuplicate();
    TestChangedOrMissingSource();
    Console.WriteLine("Todos os testes passaram.");
}
finally
{
    Directory.Delete(root, true);
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
    var result = ImageOperations.Scan(source);
    Check(result.Errors.Count == 0 && result.Images.SequenceEqual(new[] { image }), "A varredura recursiva falhou.");
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
    File.WriteAllText(a, "after");
    File.Delete(b);
    var result = ImageOperations.Move(plan, false);
    Check(result.Errors.Count == 2 && result.Moved.Count == 0 && File.ReadAllText(a) == "after",
        "Erros ou alteração após conferência não foram tratados.");
}
