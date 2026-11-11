shader_type canvas_item;

// Cor aplicada por cima da arte padrão do personagem (multiplicativa).
// Branco = arte original. Qualquer outra cor tinge a roupa/pele inteira.
// O uniform é trocado em tempo de execução (Personagem.cs), inclusive pela
// rede: cada criança escolhe a cor na tela de conta e ela aparece para todos.
uniform vec4 tint_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	vec4 cor = texture(TEXTURE, UV);
	COLOR = cor * tint_color;
}
