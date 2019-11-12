import pytest
import os
import subprocess
import ncc
import tensorflow as tf
import numpy as np

class SimpeConv2DModule(tf.Module):

  def __init__(self):
    super(SimpeConv2DModule, self).__init__()
    self.w = tf.constant(np.arange(1,10,dtype=np.float32), shape=[3,3,1,1])

  @tf.function(input_signature=[tf.TensorSpec([1,3,3,1], tf.float32)])
  def __call__(self, x):
    return tf.nn.conv2d(x, self.w, [1,1], 'SAME')

module = SimpeConv2DModule()

def init_values():
	input = np.arange(1,10,dtype=np.float32).reshape([1,1,3,3])
	ncc.save_input_array('test', input)
	ncc.save_expect_array('test', np.transpose(ncc.run_tflite(np.transpose(input, [0,2,3,1])), [0,3,1,2]))

def test_simple_conv2d():
	ncc.clear()
	ncc.save_tflite(module)
	init_values()
	ncc.compile(['--inference-type', 'float', '-t', 'cpu'])

	ncc.infer(['--dataset-format', 'raw'])
	ncc.close_to('test', 0)
	
def test_simple_conv2d_quant():
	ncc.clear()
	ncc.save_tflite(module)
	init_values()
	ncc.compile(['--inference-type', 'uint8',
	 '--dataset', ncc.input_dir + '/test.bin', '--dataset-format', 'raw',
	 '--use-float-input'])

	ncc.infer(['--dataset-format', 'raw'])
	ncc.close_to('test', 0.12)

if __name__ == "__main__":
	test_simple_conv2d()
	test_simple_conv2d_quant()