#pragma once

struct Vector3
{
	float x, y, z;
};


class Particle
{
public:
	Vector3 point;
	Vector3 a;
	Vector3 speed;
};

