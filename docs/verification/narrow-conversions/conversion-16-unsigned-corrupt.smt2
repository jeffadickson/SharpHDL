(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (not (= ((_ zero_extend 16) ((_ extract 15 0) input_0))
        (bvxor (bvand input_0 #x0000ffff) #x00000001))))

(check-sat)
(get-model)
