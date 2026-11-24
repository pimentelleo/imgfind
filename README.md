# ImgFind

Aplicativo desktop para Windows que encontra imagens em uma pasta e suas subpastas, permite visualizá-las e movê-las para uma única pasta.

## Requisitos e execução

É necessário Windows 10 ou 11 e o [SDK do .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0). No PowerShell, na pasta do repositório:

```powershell
dotnet run --project .\ImgFind\ImgFind.csproj
```

Para gerar um executável independente da instalação do .NET (Windows 64 bits):

```powershell
dotnet publish .\ImgFind\ImgFind.csproj -c Release -r win-x64 --self-contained true
```

O executável estará em `ImgFind\bin\Release\net8.0-windows\win-x64\publish\ImgFind.exe`.

## Como usar

1. Escolha a pasta de origem e clique em **Varrer imagens**. A busca inclui subpastas e mostra a pasta atual, contagens e tempo decorrido. Como o total de arquivos é desconhecido antes de percorrer as pastas, a barra é indeterminada, sem porcentagem ou estimativa inventada. Links e pontos de junção são ignorados para evitar ciclos e acesso inesperado.
2. Clique em uma linha para visualizar a imagem; marque as caixas para escolher quais mover. A visualização depende dos codecs instalados no Windows. A busca reconhece JPG, JPEG, PNG, GIF, BMP, TIFF, WebP e ICO pela extensão; arquivos inválidos ou sem codec podem aparecer na lista, mas não ser exibidos.
3. Escolha uma pasta de destino existente e clique em **Mover selecionadas** ou **Mover todas**.
4. Antes de mover, o aplicativo compara o conteúdo por SHA-256 e lista grupos de duplicatas entre os arquivos escolhidos e arquivos já presentes diretamente no destino. Escolha mover todas as cópias, deixar duplicatas fora da operação ou cancelar. Ao deixar duplicatas de fora, uma cópia selecionada é movida por grupo; se já houver cópia no destino, nenhuma cópia desse grupo é movida. Arquivos deixados de fora permanecem na origem, sem exclusão.

Durante a conferência, o painel de progresso permanece visível por pelo menos 800 ms, mesmo quando a operação termina rapidamente. O progresso e a porcentagem são relativos à etapa atual (origem ou candidatos no destino), não à operação inteira. Cada arquivo tem o mesmo peso e, durante sua leitura, conta-se a fração já lida; não é uma estimativa de tempo. A listagem do destino não tem total conhecido e usa uma barra indeterminada.

Os arquivos são reunidos diretamente no destino, sem criar subpastas. Nomes em conflito recebem sufixos como `foto (1).jpg`, sem sobrescrever arquivos existentes. Arquivos selecionados que já estão diretamente no destino não são movidos. Alterações detectadas após a conferência e falhas individuais são apresentadas no relatório; os demais arquivos continuam sendo processados. Se a conferência não puder ser concluída, a operação é cancelada antes de qualquer movimentação. Verifique o relatório ao terminar.

## Testes

Os testes de operações de arquivo não precisam de pacotes externos:

```powershell
dotnet run --project .\ImgFind.Tests\ImgFind.Tests.csproj
```
