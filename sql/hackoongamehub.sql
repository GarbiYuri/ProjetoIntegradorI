create table salas(
	id SERIAL primary key,
	name varchar(255) not null,
	descricao text
);
select * from salas;

create table usuarios (
	id SERIAL primary KEY,
	username varchar(255) unique not null,
	name varchar(255) not null,
	email varchar(255) not null,
	id_sala INTEGER references salas(id) on delete set null,
	password varchar(255),
	created_at timestamp
);

select * from usuarios;

