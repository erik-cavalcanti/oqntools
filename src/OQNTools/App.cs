using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace OQNTools
{
    public sealed class Tool
    {
        public string Id,Label,Panel,Icon,Hint;public Type CommandType;
        public Tool(string id,string label,string panel,string icon,string hint,Type type){Id=id;Label=label;Panel=panel;Icon=icon;Hint=hint;CommandType=type;}
    }
    public sealed class App:IExternalApplication
    {
        public static readonly Tool[] Catalog={
            new Tool("glb","Exportar\nGLB","Dados","glb","Exporte a vista 3D para GLB/glTF com materiais, texturas, propriedades e compressão. Motor Everse (MIT).",typeof(ExportGlb)),
            new Tool("export","Exportar\ntabelas","Dados","table","Exporte tabelas selecionadas para XLSX, preservando texto, códigos e unidades.",typeof(ExportSchedules)),
            new Tool("number","Numerar\ntabela","Dados","number","Abra uma tabela. Agrupe por vários parâmetros de tipo ou instância e atribua identificações.",typeof(NumberSchedule)),
            new Tool("transfer","Transferir\nvalores","Dados","transfer","Informe os nomes dos parâmetros de origem e destino. Busca automática em instância/tipo, prévia e suporte ao Editor de Famílias.",typeof(TransferParameters)),
            new Tool("sum","Somar\nvalores","Dados","sum","Some valores numéricos compatíveis no escopo escolhido, com unidades do projeto.",typeof(SumParameters)),
            new Tool("filter","Filtrar\nelementos","Seleção","filter","Combine regras E/OU com operadores compatíveis. Marque resultados para selecionar, isolar, ocultar ou enquadrar.",typeof(FilterElements)),
            new Tool("rooms","Por\nambiente","Seleção","room","Localize elementos pelo ponto de inserção/ponto médio dentro de ambientes ou espaços.",typeof(FindInRooms)),
            new Tool("box","Caixa\n3D","Seleção","box","Selecione elementos e abra uma nova vista 3D com caixa de corte em torno deles.",typeof(SectionGlance)),
            new Tool("pipes","Pintar\ntubos","Representação","pipe","Colorização de tubos (Exclusivo para alunos OQN). Requer o procedimento de substituição do segmento, criação de material e posterior exclusão ensinado no curso OQN.",typeof(PaintPipes)),
            new Tool("conduits","Pintar\nconduítes","Representação","conduit","Configure o campo Material nas propriedades de tipo dos conduítes. Aplicação automática, sem pintar conexões.",typeof(PaintConduits)),
            new Tool("filters","ViewFilter\nCopiar filtros","Representação","layers","Copie filtros, cores, ordem, ativação e visibilidade entre vistas ou modelos de vista do mesmo documento.",typeof(TransferFilters)),
            new Tool("dash","Tracejar\npor altura","Representação","dash","Escolha nível, faixa de altura e padrão. As demais sobreposições do elemento são preservadas.",typeof(DashByLevel)),
            new Tool("dimensions","Substituir\ncotas","Documentação","dimensions","Troque referências RVT ou DWG/DXF vinculadas por linhas de detalhe locais na vista ativa ou em vistas escolhidas. Confira a prévia; as medidas convertidas deixam de acompanhar o vínculo.",typeof(ReplaceLinkedDimensions)),
            new Tool("tag","Identificar\ntubos","Documentação","tag","Selecione tipo de tag, comprimento mínimo, orientação horizontal/vertical e tolerância angular.",typeof(TagPipes)),
            new Tool("rename","Renomear\nem lote","Documentação","rename","Renomeie vistas, folhas, famílias, tipos ou materiais com prefixo, sufixo e substituição.",typeof(BulkRename)),
            new Tool("views","Duplicar\nvistas","Documentação","copy","Duplique vistas com ou sem detalhamento, ou como dependentes, sem depender do Navegador de Projeto.",typeof(DuplicateViews)),
            new Tool("sheets","Duplicar\nfolhas","Documentação","sheets","Copie carimbos e vistas posicionadas. Legendas e tabelas são reutilizadas; detalhes diretos da folha não são copiados.",typeof(DuplicateSheets)),
            new Tool("createSheets","Criar\nfolhas","Documentação","new_sheet","Crie uma folha por vista escolhida e posicione a vista no centro do carimbo.",typeof(SheetsFromViews)),
            new Tool("alignViews","Alinhar\nviewports","Documentação","align","Alinhe o centro das caixas de viewports por X, Y ou ambos, nas coordenadas da folha.",typeof(AlignViewports)),
            new Tool("families","Gerenciar\nfamílias","Famílias","family","Importe RFA ou exporte famílias editáveis por categoria. Arquivos existentes são preservados.",typeof(ManageFamilies)),
            new Tool("workset","Atribuir\nworkset","Famílias","workset","Atribua elementos a um workset. Combine com Filtrar elementos para criar regras de seleção.",typeof(AssignWorkset)),
            new Tool("shared","Adicionar\nparâmetros","Famílias","parameters","No Editor de Famílias, adicione parâmetros compartilhados a partir de um arquivo TXT.",typeof(AddSharedParameters)),
            new Tool("push","Campos da\ntabela","Famílias","push","Adicione os parâmetros compartilhados da tabela às famílias carregáveis presentes nela.",typeof(PushScheduleParameters)),
            new Tool("splitPipes","Split Pipes\nCortar tubos","MEP","split_pipes","Corte no projeto inteiro por tipo e comprimento em metros. Insere luvas pelas preferências de roteamento e mantém a sobra.",typeof(SplitPipes)),
            new Tool("bloom","Prolongar\nconectores","MEP","bloom","Escolha o tipo de tubo e o comprimento em mm. Cada conector livre fornece seu próprio diâmetro e direção.",typeof(Bloom)),
            new Tool("move","Mover e\nconectar","MEP","connect","Escolha o destino e a peça a mover. Move e conecta sem solicitar rotação. O Revit ajusta as peças ligadas. Não exige dimensões iguais.",typeof(MoveConnect)),
            new Tool("align3d","Mover, conectar\ne alinhar","MEP","align3d","Escolha o destino e a peça móvel. Move a peça, orienta seus conectores e conecta na mesma operação. O Revit ajusta as peças ligadas, sem exigir dimensões iguais.",typeof(MoveConnectAlign)),
            new Tool("disconnect","Desconectar","MEP","disconnect","Selecione dois elementos diretamente conectados para desfazer a ligação entre eles.",typeof(Disconnect)),
            new Tool("up","Curva\npara cima","Curvas e ramais","up","Clique perto da extremidade livre de um trecho reto e informe o comprimento do novo trecho.",typeof(ElbowUp)),
            new Tool("down","Curva\npara baixo","Curvas e ramais","down","Crie um joelho para baixo com as preferências de roteamento do tipo.",typeof(ElbowDown)),
            new Tool("down45","Curva\nbaixo 45°","Curvas e ramais","down45","A partir de trecho com projeção horizontal, crie saída descendente a 45°.",typeof(ElbowDown45)),
            new Tool("left","Curva\nà esquerda","Curvas e ramais","left","Crie curva à esquerda do eixo, no plano da vista ativa.",typeof(ElbowLeft)),
            new Tool("right","Curva\nà direita","Curvas e ramais","right","Crie curva à direita do eixo, no plano da vista ativa.",typeof(ElbowRight)),
            new Tool("branch","Alinhar\nramal +","Curvas e ramais","branch","Altera a direção do ramal para perpendicular em planta e ajusta sua declividade à do principal. Prepara o encontro por aparar/estender, mantendo as ligações existentes.",typeof(AlignBranch)),
            new Tool("branchLite","Alinhar\nramal LT","Curvas e ramais","branch_lite","Move o ramal pelo menor deslocamento entre os eixos em 3D, preservando direção e inclinação, sem conectar. O Revit ajusta os elementos ligados; não desloca a rede inteira.",typeof(AlignBranchLite)),
            new Tool("rotate","Girar\nconexão","Curvas e ramais","rotate","Selecione qualquer elemento com conectores: peças, acessórios, equipamentos, dispositivos ou trechos MEP. Escolha o eixo e o ângulo, mesmo com o elemento conectado. O Revit trata as ligações afetadas.",typeof(RotateFitting)),
            new Tool("rotate180","Girar\n180°","Curvas e ramais","rotate180","Gire qualquer elemento com conectores em 180° ao redor do eixo escolhido, sem exigir desconexão prévia.",typeof(Rotate180)),
            new Tool("flip","Inverter\nplano","Curvas e ramais","flip","Inverta o plano de trabalho das famílias selecionadas que permitem essa operação.",typeof(FlipWorkPlane)),
            new Tool("section","Corte por\ntrecho","Curvas e ramais","section","Crie e abra um corte paralelo a um trecho reto, com altura e profundidade definidas.",typeof(SectionByMep)),
            new Tool("deleteSystem","Desconectar\ndo sistema","Limpeza","system","Desconecte apenas os elementos selecionados dos vizinhos. Mantém posição e não exclui sistemas ou elementos.",typeof(DeleteSystem)),
            new Tool("purge","Remover vírus\nfase existente","Limpeza","clean","Remova aparências Fase - Existente que o Revit confirma como não utilizadas. Não apaga fases ou materiais.",typeof(PurgeExistingAppearance)),
            new Tool("backups","Arquivar\nbackups","Limpeza","archive","Mova backups RFA numerados para uma pasta de arquivo preservando a estrutura de diretórios.",typeof(ArchiveBackups)),
            new Tool("help","Guia de\nuso","Ajuda","help","Abra o guia de uso do OQN Tools.",typeof(HelpCommand))
        };
        ConduitMaterialUpdater conduitUpdater;
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
            string expected=
#if REVIT2022
                "2022";
#elif REVIT2023
                "2023";
#elif REVIT2024
                "2024";
#elif REVIT2025
                "2025";
#elif REVIT2027
                "2027";
#else
                "2026";
#endif
            if(app.ControlledApplication.VersionNumber!=expected){TaskDialog.Show("OQN Tools","DLL destinada ao Revit "+expected+". Instale o pacote correto.");return Result.Failed;}
            try{app.CreateRibbonTab("OQN Tools");}catch(Autodesk.Revit.Exceptions.ArgumentException){}
            string assembly=Assembly.GetExecutingAssembly().Location;string folder=Path.GetDirectoryName(assembly);
            foreach(var group in Catalog.GroupBy(t=>t.Panel))
            {
                var panel=app.CreateRibbonPanel("OQN Tools",group.Key);
                // Keep the ribbon compact: two principal buttons followed by a menu.
                var tools=group.ToList();PulldownButton menu=null;
                for(int i=0;i<tools.Count;i++)
                {
                    var tool=tools[i];var data=new PushButtonData("OQN_"+tool.Id,tool.Label,assembly,tool.CommandType.FullName);
                    PushButton button;
                    if(i<2)button=(PushButton)panel.AddItem(data);
                    else{if(menu==null)menu=(PulldownButton)panel.AddItem(new PulldownButtonData("OQN_MORE_"+group.Key,"Mais\nferramentas"));button=menu.AddPushButton(data);}
                    button.ToolTip=tool.Hint;button.LongDescription="OQN Tools · Beta, desenvolvido junto com os usuários.\n"+tool.Hint;
                    button.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url,new Uri(Path.Combine(folder,"Manual.html")).AbsoluteUri));
                    string icon=Path.Combine(folder,"assets",tool.Icon+"-32.png");if(File.Exists(icon))button.LargeImage=new BitmapImage(new Uri(icon));
                    icon=Path.Combine(folder,"assets",tool.Icon+"-16.png");if(File.Exists(icon))button.Image=new BitmapImage(new Uri(icon));
                }
            }
            conduitUpdater=ConduitMaterialUpdater.Register(app);
            CompatibilityDiagnostics.TryWrite(app.ControlledApplication.VersionNumber,"startup-complete");
            return Result.Succeeded;
            }
            catch(Exception ex)
            {
                CompatibilityDiagnostics.TryWrite(app.ControlledApplication.VersionNumber,"startup-failed",ex);
                throw;
            }
        }
        public Result OnShutdown(UIControlledApplication app){if(conduitUpdater!=null)Autodesk.Revit.DB.UpdaterRegistry.UnregisterUpdater(conduitUpdater.GetUpdaterId());return Result.Succeeded;}
    }
    [Transaction(TransactionMode.Manual)] public sealed class HelpCommand:Command
    {
        protected override void Run(Context c){string path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"Manual.html");Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    }
}
