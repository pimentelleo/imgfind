# ImgFind

Aplicativo desktop para Windows que encontra fotos e vídeos em uma pasta e suas subpastas, permite visualizá-los e movê-los para subpastas organizadas por extensão.

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

1. Escolha a pasta de origem e clique em **Varrer arquivos**. A busca inclui subpastas e mostra a pasta atual, contagens e tempo decorrido. Como o total de arquivos é desconhecido antes de percorrer as pastas, a barra é indeterminada, sem porcentagem ou estimativa inventada. Links e pontos de junção são ignorados para evitar ciclos e acesso inesperado.
2. Clique em uma linha para visualizar uma foto ou use os controles para reproduzir, pausar e reiniciar um vídeo; marque as caixas para escolher quais mover. A prévia depende dos codecs e recursos de mídia instalados no Windows. Arquivos inválidos ou formatos e codecs não disponíveis podem aparecer na lista, mas não ser reproduzidos; a aplicação informa o erro. A busca reconhece fotos JPG, JPEG, PNG, GIF, BMP, TIF, TIFF, WebP e ICO e vídeos MP4, MOV, M4V, MKV, AVI, WMV, WebM, MPEG, MPG, 3GP, MTS e M2TS pela extensão.
3. Escolha uma pasta de destino existente e clique em **Mover selecionados** ou **Mover todos**.
4. Antes de mover, o aplicativo compara o conteúdo por SHA-256 e lista grupos de duplicatas entre os arquivos escolhidos e arquivos já presentes diretamente no destino ou nas suas subpastas de tipos reconhecidos, independentemente da extensão. Escolha mover todas as cópias, deixar duplicatas fora da operação ou cancelar. Ao deixar duplicatas de fora, uma cópia selecionada é movida por grupo; se já houver cópia no destino, nenhuma cópia desse grupo é movida. Arquivos deixados de fora permanecem na origem, sem exclusão.

Durante a conferência, o painel de progresso permanece visível por pelo menos 800 ms, mesmo quando a operação termina rapidamente. O progresso e a porcentagem são relativos à etapa atual (origem ou candidatos no destino), não à operação inteira. Cada arquivo tem o mesmo peso e, durante sua leitura, conta-se a fração já lida; não é uma estimativa de tempo. A listagem do destino não tem total conhecido e usa uma barra indeterminada.

Fotos e vídeos são reunidos em subpastas do destino por extensão, como `JPG`, `PNG`, `MP4` e `MOV` (maiúsculas e minúsculas da extensão usam a mesma pasta). A extensão é mantida no nome original; `JPG` e `JPEG` são pastas distintas. A conferência inclui arquivos na raiz do destino para reconhecer cópias de versões anteriores, mas não percorre outras subpastas arbitrárias. Pastas de tipo que sejam links ou pontos de junção são recusadas. Nomes em conflito na mesma pasta recebem sufixos como `foto (1).jpg`, sem sobrescrever arquivos existentes. Arquivos selecionados que já estão na subpasta correspondente não são movidos. Alterações detectadas após a conferência e falhas individuais são apresentadas no relatório; os demais arquivos continuam sendo processados. Se a conferência não puder ser concluída, a operação é cancelada antes de qualquer movimentação. Verifique o relatório ao terminar. Vídeos grandes são conferidos por blocos, sem carregar o arquivo inteiro na memória.

## Testes

Os testes de operações de arquivo não precisam de pacotes externos:

```powershell
dotnet run --project .\ImgFind.Tests\ImgFind.Tests.csproj
```
