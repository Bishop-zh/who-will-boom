Shader "BombTavern/RuntimeParticle"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            fixed4 frag(Output input) : SV_Target
            {
                float2 offset = input.uv * 2 - 1;
                fixed4 color = input.color;
                color.a *= pow(saturate(1 - dot(offset, offset)), 2);
                return color;
            }
            ENDCG
        }
    }
}
