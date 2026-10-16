using System;
using System.IO;
using System.Linq;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.EditorTools
{
    public static class StatisticsPrefabBuilder
    {
        const string Sheet = "Assets/Resources/BombTavern/UI/StatisticsSheet.png";
        const string Prefab = "Assets/Resources/BombTavern/Prefabs/UI/StatisticsPopup.prefab";
        static Sprite[] sprites;
        static TMP_FontAsset font;
        static readonly Color Gold = new Color(1, .79f, .34f), Cream = new Color(1, .92f, .77f);
        [MenuItem("工具/炸弹酒馆/16. 创建战绩统计预制体", false, 26)]
        public static void CreateMissing() { if (!File.Exists(Prefab)) Build(); }
        public static void BuildAndPlayer() { CreateMissing(); LegacyUiPrefabBuilder.CreateMissing(); StatisticsAcceptance.Run(); PlayerBuilder.BuildWindows(); }
        public static void Build()
        {
            if (!File.Exists(Sheet)) throw new FileNotFoundException("缺少战绩统计图集", Sheet);
            AssetDatabase.ImportAsset(Sheet);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Sheet);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.spritesheet = new[] {
                Slice("StatsLogo",230,0,790,354), Slice("StatsSwords",50,360,190,160), Slice("StatsCup",250,360,180,160),
                Slice("StatsPie",450,354,180,168), Slice("StatsFlame",642,354,184,168), Slice("StatsHistory",832,354,176,170),
                Slice("StatsChart",1025,354,185,170), Slice("StatsCrown",45,520,196,145), Slice("StatsSkull",257,520,175,145),
                Slice("StatsClose",455,520,163,147), Slice("StatsRefresh",637,520,192,147),
                Slice("StatsSingle",852,520,156,147), Slice("StatsMulti",1020,520,195,147), Slice("StatsPanel",20,666,1220,572,40) };
            importer.SaveAndReimport(); sprites = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>().ToArray();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/BombTavern/UI/Fonts/NotoSansSC-Dynamic.asset");
            var root = Rect(null, "StatisticsPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.gameObject.AddComponent<StatisticsPopupView>();
            Image(root, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null, new Color(0,0,0,.72f), true);
            var panel = Rect(root, "StatisticsPanel", Vector2.one * .5f, Vector2.one * .5f, new Vector2(0,-20), new Vector2(1800,1240));
            panel.gameObject.AddComponent<CanvasGroup>(); panel.gameObject.AddComponent<UiModalAnimator>();
            Image(panel, "Frame", Vector2.one*.5f, Vector2.one*.5f, new Vector2(0,-45), new Vector2(1800,1100), "StatsPanel", Color.white).GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            Image(panel, "Logo", Vector2.one*.5f, Vector2.one*.5f, new Vector2(0,470), new Vector2(810,360), "StatsLogo", Color.white);
            Text(panel,"Subtitle","查看对局表现与数据分析",new Vector2(0,320),new Vector2(620,55),24,Gold);
            panel.Find("Subtitle").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.Midline;
            Image(panel,"Close",Vector2.one*.5f,Vector2.one*.5f,new Vector2(815,420),new Vector2(78,78),"StatsClose",Color.white,true);
            string[] names = { "Total", "Wins", "Rate", "Streak" }, titles = { "总对局", "胜场", "胜率", "最高连胜" }, icons = { "StatsSwords", "StatsCup", "StatsPie", "StatsFlame" };
            for (int i=0;i<4;i++)
            {
                var card = Box(panel,"Summary_"+i,new Vector2(-630+420*i,205),new Vector2(390,145));
                Icon(card, icons[i], new Vector2(-125,0),new Vector2(94,94));
                Text(card,names[i]+"Title",titles[i],new Vector2(30,35),new Vector2(230,46),27,Cream);
                Text(card,names[i]+"Value","0",new Vector2(30,-28),new Vector2(230,100),48,Gold);
            }
            var history = Box(panel,"RecentPanel",new Vector2(-455,-210),new Vector2(790,655));
            Icon(history,"StatsHistory",new Vector2(-340,280),new Vector2(60,60));
            Text(history,"RecentTitle","近期战绩",new Vector2(-180,280),new Vector2(230,60),33,Gold);
            Text(history,"RecentCount","最近10场对局",new Vector2(245,280),new Vector2(235,52),23,Cream);
            for (int i=0;i<10;i++)
            {
                var row=Box(history,"Recent_"+i,new Vector2(0,207-i*50),new Vector2(742,46),true);
                ResultTag(row);
                Icon(row,"StatsCrown",new Vector2(-329,0),new Vector2(39,39),"Icon");
                Text(row,"Result","胜利",new Vector2(-244,0),new Vector2(100,40),25,Gold);
                Text(row,"Mode","单人模式",new Vector2(-63,0),new Vector2(155,40),24,Cream);
                Text(row,"Time","刚刚",new Vector2(206,0),new Vector2(172,40),23,Cream);
                Text(row,"Arrow","›",new Vector2(344,0),new Vector2(34,42),22,Gold);
            }
            Text(history,"Empty","暂无战绩\n完成一局后，成绩会自动记录在这里。",new Vector2(0,20),new Vector2(640,180),30,Cream);
            var analysis=Box(panel,"AnalysisPanel",new Vector2(410,-210),new Vector2(890,655));
            Icon(analysis,"StatsChart",new Vector2(-380,280),new Vector2(60,60));
            Text(analysis,"AnalysisTitle","数据分析",new Vector2(-215,280),new Vector2(240,60),33,Gold);
            Text(analysis,"RingTitle","胜率统计",new Vector2(-256,165),new Vector2(240,55),30,Gold);
            var ring=Rect(analysis,"WinRing",Vector2.one*.5f,Vector2.one*.5f,new Vector2(-247,-20),new Vector2(290,290));
            var graphic=ring.gameObject.AddComponent<StatisticsRingGraphic>(); graphic.color=Gold; graphic.raycastTarget=false;
            Text(analysis,"RingValue","—",new Vector2(-247,-7),new Vector2(210,110),49,Gold);
            Text(analysis,"RingCaption","胜率",new Vector2(-247,-67),new Vector2(150,48),27,Cream);
            Text(analysis,"WinsLegend","● 胜场  0",new Vector2(-330,-218),new Vector2(185,45),23,Gold);
            Text(analysis,"LossLegend","● 负场  0",new Vector2(-133,-218),new Vector2(185,45),23,new Color(.62f,.53f,.47f));
            Image(analysis,"Divider",Vector2.one*.5f,Vector2.one*.5f,new Vector2(-30,-10),new Vector2(2,460),null,new Color(.58f,.37f,.14f));
            Text(analysis,"ModesTitle","模式对局分布",new Vector2(205,165),new Vector2(355,55),30,Gold);
            BuildMode(analysis,"Single","单人模式","StatsSingle",45,Gold);
            BuildMode(analysis,"Multi","多人模式","StatsMulti",-107,new Color(1,.48f,.12f));
            var refresh=Box(analysis,"Refresh",new Vector2(263,-264),new Vector2(270,65),true);
            Icon(refresh,"StatsRefresh",new Vector2(-87,0),new Vector2(49,49));
            Text(refresh,"RefreshLabel","刷新数据",new Vector2(32,0),new Vector2(172,55),27,Gold);
            Text(panel,"Status","本机记录 · 仅统计完成的对局",new Vector2(0,-582),new Vector2(1600,46),23,Cream);
            var details=Box(panel,"Details",new Vector2(0,-70),new Vector2(800,660));
            Text(details,"DetailText","",new Vector2(0,25),new Vector2(690,500),31,Cream);
            var close=Box(details,"DetailsClose",new Vector2(0,-262),new Vector2(240,60),true);
            Text(close,"Caption","返回统计",Vector2.zero,new Vector2(220,50),27,Gold); details.gameObject.SetActive(false);
            try { PrefabUtility.SaveAsPrefabAsset(root.gameObject,Prefab); AssetDatabase.SaveAssets(); }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
            Debug.Log("[战绩统计] 可编辑 UGUI 预制体已保存。");
        }
        static void BuildMode(Transform parent,string prefix,string title,string icon,float y,Color tint)
        {
            Icon(parent,icon,new Vector2(50,y),new Vector2(56,56));
            Text(parent,prefix+"Title",title,new Vector2(145,y),new Vector2(128,58),25,Cream);
            var track=Image(parent,prefix+"Track",Vector2.one*.5f,Vector2.one*.5f,new Vector2(276,y),new Vector2(118,32),null,new Color(.28f,.21f,.17f));
            var fill=Image(track,prefix+"Fill",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,null,tint).GetComponent<Image>();
            fill.sprite=SolidSprite(); fill.type=UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillOrigin=0; fill.fillAmount=0;
            Text(parent,prefix+"Value","0 场\n0%",new Vector2(395,y),new Vector2(102,80),23,Cream);
        }
        static Sprite SolidSprite()
        {
            const string path = "Assets/Resources/BombTavern/UI/StatisticsSolid.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(4,4,TextureFormat.RGBA32,false);
                texture.SetPixels(Enumerable.Repeat(Color.white,16).ToArray()); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path); var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Sprite; importer.mipmapEnabled=false; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        public static void RepairAndBuild()
        {
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                Transform Node(string name) => root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
                root.GetComponent<StatisticsPopupView>().preferredScale=1.08f;
                Node("DetailText").GetComponent<TMP_Text>().richText=false;
                var subtitle=Node("Subtitle").GetComponent<TMP_Text>(); subtitle.rectTransform.anchoredPosition=new Vector2(0,320); subtitle.rectTransform.sizeDelta=new Vector2(620,55); subtitle.fontSize=24; subtitle.alignment=TextAlignmentOptions.Midline;
                foreach(var name in new[]{"TotalValue","WinsValue","RateValue","StreakValue"}) { var rect=Node(name).GetComponent<RectTransform>(); rect.sizeDelta=new Vector2(rect.sizeDelta.x,100); }
                Node("RingValue").GetComponent<RectTransform>().sizeDelta=new Vector2(210,110);
                Node("LossLegend").GetComponent<TMP_Text>().color=new Color(.62f,.53f,.47f);
                foreach(var label in root.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.name=="Arrow")) label.fontSize=22;
                foreach(var name in new[]{"SingleFill","MultiFill"}) Node(name).GetComponent<UnityEngine.UI.Image>().sprite=SolidSprite();
                foreach(var rect in root.GetComponentsInChildren<RectTransform>(true).Where(r=>r.name.StartsWith("Summary_") || r.name=="RecentPanel" || r.name=="AnalysisPanel" || r.name=="Details"))
                    rect.GetComponent<UnityEngine.UI.Image>().sprite=CardSprite();
                for(int i=0;i<10;i++) { var row=Node("Recent_"+i); row.GetComponent<UnityEngine.UI.Image>().sprite=CardSprite(); if(row.Find("ResultTag")==null) ResultTag(row); }
                PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            const string rules="Assets/Resources/BombTavern/Prefabs/UI/GameSetupRules.prefab";
            root=PrefabUtility.LoadPrefabContents(rules);
            try { var rect=(RectTransform)root.transform; rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.sizeDelta=Vector2.zero; PrefabUtility.SaveAsPrefabAsset(root,rules); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            foreach(var name in new[]{"GameSetupRules","GameOver","LobbyPanel"})
            {
                string path="Assets/Resources/BombTavern/Prefabs/UI/"+name+".prefab"; root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
                    foreach(var button in root.GetComponentsInChildren<Button>(true)) if(button.GetComponent<UiButtonMotion>()==null) button.gameObject.AddComponent<UiButtonMotion>();
                    var body=root.GetComponentsInChildren<Transform>(true).First(t=>t.name==(name=="GameSetupRules"?"GameSetupPanel":name=="GameOver"?"ResultCard":"LobbyPanel"));
                    body.GetComponent<UnityEngine.UI.Image>().sprite=CardSprite();
                    if(name=="GameOver")
                    {
                        var columns=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Columns");
                        foreach(var label in columns.GetComponentsInChildren<TMP_Text>(true)) label.fontSize=18;
                        var crown=root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.text=="♛");
                        if(crown!=null)
                        {
                            crown.text="";
                            var icon=Image(crown.transform,"Crown",Vector2.one*.5f,Vector2.one*.5f,Vector2.zero,new Vector2(50,44),null,Color.white);
                            icon.GetComponent<UnityEngine.UI.Image>().sprite=Resources.LoadAll<Sprite>("BombTavern/UI/StatisticsSheet").First(s=>s.name=="StatsCrown");
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets(); StatisticsAcceptance.Run(); PlayerBuilder.BuildWindows();
        }
        static void ResultTag(Transform row)
        {
            var tag=Image(row,"ResultTag",Vector2.one*.5f,Vector2.one*.5f,new Vector2(-281,0),new Vector2(180,46),null,new Color(.68f,.43f,.12f));
            tag.GetComponent<UnityEngine.UI.Image>().sprite=SolidSprite(); tag.SetAsFirstSibling();
        }
        internal static Sprite CardSprite()
        {
            const string path="Assets/Resources/BombTavern/UI/StatisticsCard.png";
            if(!File.Exists(path))
            {
                var sprite=UiKit.ChamferSprite(new Color(.19f,.105f,.052f,1),new Color(.78f,.49f,.18f),9,2);
                File.WriteAllBytes(path,sprite.texture.EncodeToPNG()); AssetDatabase.ImportAsset(path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.textureType=TextureImporterType.Sprite; importer.spriteBorder=sprite.border; importer.mipmapEnabled=false; importer.alphaIsTransparency=true; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static SpriteMetaData Slice(string name,int x,int top,int w,int h,int border=0)=>new SpriteMetaData {name=name,rect=new UnityEngine.Rect(x,1254-top-h,w,h),pivot=Vector2.one*.5f,alignment=9,border=new Vector4(border,border,border,border)};
        static RectTransform Rect(Transform parent,string name,Vector2 min,Vector2 max,Vector2 position,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        static RectTransform Image(Transform parent,string name,Vector2 min,Vector2 max,Vector2 position,Vector2 size,string sprite,Color tint,bool button=false)
        {var r=Rect(parent,name,min,max,position,size);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=tint;image.raycastTarget=button;if(sprite!=null)image.sprite=sprites.First(s=>s.name==sprite);image.type=image.sprite!=null&&image.sprite.border!=Vector4.zero?UnityEngine.UI.Image.Type.Sliced:UnityEngine.UI.Image.Type.Simple;if(button)r.gameObject.AddComponent<Button>().targetGraphic=image;return r;}
        static RectTransform Box(Transform parent,string name,Vector2 position,Vector2 size,bool button=false)
        {var r=Image(parent,name,Vector2.one*.5f,Vector2.one*.5f,position,size,null,Color.white,button);var image=r.GetComponent<UnityEngine.UI.Image>();image.sprite=CardSprite();image.type=UnityEngine.UI.Image.Type.Sliced;image.raycastTarget=true;return r;}
        static void Icon(Transform parent,string sprite,Vector2 position,Vector2 size,string name=null)=>Image(parent,name??sprite,Vector2.one*.5f,Vector2.one*.5f,position,size,sprite,Color.white);
        static void Text(Transform parent,string name,string value,Vector2 position,Vector2 size,float fontSize,Color tint)
        {var r=Rect(parent,name,Vector2.one*.5f,Vector2.one*.5f,position,size);var label=r.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=value;label.color=tint;label.fontSize=fontSize;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;label.enableWordWrapping=true;}
    }
}
